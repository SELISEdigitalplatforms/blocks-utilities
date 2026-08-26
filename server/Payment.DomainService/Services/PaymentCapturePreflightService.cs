using FluentValidation;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class PaymentCapturePreflightService :
    IPaymentCapturePreflightService
{
    private readonly IValidator<CreatePaymentCaptureRequest> _validator;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentRateLimiter _rateLimiter;
    private readonly IPaymentCaptureRepository _captures;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;

    public PaymentCapturePreflightService(
        IValidator<CreatePaymentCaptureRequest> validator,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentRateLimiter rateLimiter,
        IPaymentCaptureRepository captures,
        IPaymentRepository payments,
        IPaymentProviderCache providers)
    {
        _validator = validator;
        _minorUnits = minorUnits;
        _rateLimiter = rateLimiter;
        _captures = captures;
        _payments = payments;
        _providers = providers;
    }

    public async Task<PaymentCapturePreflightResult> ExecuteAsync(
        string paymentDetailId,
        CreatePaymentCaptureRequest request,
        string idempotencyKey,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(paymentDetailId, out _))
        {
            return Failed(
                PaymentFailureKind.Validation,
                "invalid_payment_id",
                "The payment identifier is invalid.",
                correlationId);
        }

        var validation = await _validator.ValidateAsync(
            request,
            cancellationToken);

        if (!validation.IsValid)
        {
            var fields = validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(error => error.ErrorMessage)
                        .Distinct()
                        .ToArray());

            return Failed(
                PaymentFailureKind.Validation,
                "payment_capture_validation_failed",
                "The capture request is invalid.",
                correlationId,
                fields);
        }

        if (!Guid.TryParse(idempotencyKey, out _) ||
            idempotencyKey.Length > 64)
        {
            return Failed(
                PaymentFailureKind.Validation,
                "invalid_idempotency_key",
                "Idempotency-Key must be a UUID with at most 64 characters.",
                correlationId);
        }

        var payment = await _captures.GetPaymentAsync(
            context.TenantId,
            paymentDetailId,
            cancellationToken);

        if (payment == null)
        {
            return Failed(
                PaymentFailureKind.NotFound,
                "payment_not_found",
                "The payment was not found.",
                correlationId);
        }

        // See the refund preflight: a card setup looks capturable and holds nothing to capture.
        if (string.Equals(
                payment.PaymentFlow,
                PaymentFlows.PaymentMethodSetup,
                StringComparison.Ordinal) ||
            payment.PaymentStatus is not
                (PaymentStatuses.Authorized or
                 PaymentStatuses.PartiallyCaptured) ||
            string.IsNullOrWhiteSpace(payment.PspReference))
        {
            return Failed(
                PaymentFailureKind.Conflict,
                "payment_not_capturable",
                "The payment is not available for capture.",
                correlationId);
        }

        if (payment.CaptureMode ==
            PaymentCaptureModes.AutomaticImmediate)
        {
            return Failed(
                PaymentFailureKind.Conflict,
                "payment_capture_is_automatic",
                "This payment is configured for immediate automatic capture.",
                correlationId);
        }

        var available = payment.AuthorizedAmount -
                        payment.CapturedAmount -
                        payment.ReservedCaptureAmount;

        if (request.Amount > available)
        {
            return Failed(
                PaymentFailureKind.Conflict,
                "payment_capture_amount_unavailable",
                "The requested amount is not available for capture.",
                correlationId);
        }

        if (!_minorUnits.TryConvert(
                request.Amount,
                payment.CurrencyCode,
                out var minorUnits))
        {
            return Failed(
                PaymentFailureKind.Validation,
                "unsupported_currency_or_precision",
                "The capture amount has invalid precision.",
                correlationId);
        }

        var rateLimit = await _rateLimiter.CheckAsync(
            context.TenantId,
            context.ActorId,
            $"capture:{paymentDetailId}",
            cancellationToken);

        if (!rateLimit.IsAvailable || !rateLimit.IsAllowed)
        {
            return Failed(
                rateLimit.IsAvailable
                    ? PaymentFailureKind.RateLimited
                    : PaymentFailureKind.Unavailable,
                rateLimit.IsAvailable
                    ? "payment_capture_rate_limit_exceeded"
                    : "payment_rate_limiter_unavailable",
                rateLimit.IsAvailable
                    ? "Too many capture requests."
                    : "Capture protection is temporarily unavailable.",
                correlationId,
                rateLimit: rateLimit);
        }

        // From the payment, so a capture resolves the configuration that took the money
        // rather than whichever organization the caller happens to belong to.
        var provider = await _providers.GetAsync(
            context.TenantId,
            payment.OrganizationId,
            payment.ProviderName,
            () => _payments.GetProviderAsync(
                context.TenantId,
                payment.OrganizationId,
                payment.ProviderName,
                cancellationToken));

        if (provider == null ||
            !provider.IsEnabled ||
            string.IsNullOrWhiteSpace(provider.ApiKey) ||
            string.IsNullOrWhiteSpace(provider.MerchantId))
        {
            return Failed(
                PaymentFailureKind.Unavailable,
                "payment_provider_unavailable",
                "The payment provider is temporarily unavailable.",
                correlationId,
                rateLimit: rateLimit);
        }

        return new PaymentCapturePreflightResult(
            minorUnits,
            rateLimit,
            payment,
            provider,
            null);
    }

    private static PaymentCapturePreflightResult Failed(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId,
        Dictionary<string, string[]>? fields = null,
        PaymentRateLimitResult? rateLimit = null) =>
        new(
            0,
            rateLimit,
            null,
            null,
            PaymentCaptureOperationResult.Failure(
                failureKind,
                errorCode,
                errorMessage,
                correlationId,
                fields,
                rateLimit));
}
