using FluentValidation;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class PaymentRefundPreflightService :
    IPaymentRefundPreflightService
{
    private readonly IValidator<CreatePaymentRefundRequest>
        _validator;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentRateLimiter _rateLimiter;
    private readonly IPaymentRefundRepository _refunds;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly IPaymentFundReturnStrategyResolver _strategies;
    private readonly TimeProvider _time;

    public PaymentRefundPreflightService(
        IValidator<CreatePaymentRefundRequest> validator,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentRateLimiter rateLimiter,
        IPaymentRefundRepository refunds,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        IPaymentFundReturnStrategyResolver strategies,
        TimeProvider? time = null)
    {
        _validator = validator;
        _minorUnits = minorUnits;
        _rateLimiter = rateLimiter;
        _refunds = refunds;
        _payments = payments;
        _providers = providers;
        _strategies = strategies;
        _time = time ?? TimeProvider.System;
    }

    public async Task<PaymentRefundPreflightResult>
        ExecuteAsync(
            string paymentDetailId,
            CreatePaymentRefundRequest request,
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

        var validation =
            await _validator.ValidateAsync(
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
                "payment_refund_validation_failed",
                "The refund request is invalid.",
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

        var payment = await _refunds.GetPaymentAsync(
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

        // A card setup settles at Authorized with a provider reference, so it satisfies every
        // condition below without ever having taken a penny. Refunding one would ask the provider
        // to return money it was never sent.
        if (string.Equals(
                payment.PaymentFlow,
                PaymentFlows.PaymentMethodSetup,
                StringComparison.Ordinal) ||
            payment.PaymentStatus is not
                (PaymentStatuses.Authorized or
                 PaymentStatuses.Captured or
                 PaymentStatuses.PartiallyCaptured or
                 PaymentStatuses.PartiallyRefunded) ||
            string.IsNullOrWhiteSpace(payment.PspReference))
        {
            return Failed(
                PaymentFailureKind.Conflict,
                "payment_not_refundable",
                "The payment is not refundable.",
                correlationId);
        }

        var strategy = _strategies.Resolve(
            payment,
            request.Amount);

        if (!strategy.IsAllowed)
        {
            return Failed(
                PaymentFailureKind.Conflict,
                strategy.ErrorCode!,
                strategy.ErrorMessage!,
                correlationId);
        }

        if (!_minorUnits.TryConvert(
                request.Amount,
                payment.CurrencyCode,
                out var amountMinorUnits))
        {
            return Failed(
                PaymentFailureKind.Validation,
                "unsupported_currency_or_precision",
                "The refund amount has invalid precision.",
                correlationId);
        }

        var rateLimit = await _rateLimiter.CheckAsync(
            context.TenantId,
            context.ActorId,
            $"refund:{paymentDetailId}",
            cancellationToken);

        if (!rateLimit.IsAvailable)
        {
            return Failed(
                PaymentFailureKind.Unavailable,
                "payment_rate_limiter_unavailable",
                "Refund protection is temporarily unavailable.",
                correlationId,
                rateLimit: rateLimit);
        }

        if (!rateLimit.IsAllowed)
        {
            return Failed(
                PaymentFailureKind.RateLimited,
                "payment_refund_rate_limit_exceeded",
                "Too many refund requests.",
                correlationId,
                rateLimit: rateLimit);
        }

        // From the payment, so a refund returns money through the configuration that took it.
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

        var paymentDate =
            payment.WebhookConfirmedAtUtc ??
            payment.PaymentDate;

        if (provider.MaxRefundDays > 0 &&
            paymentDate != default &&
            paymentDate.AddDays(provider.MaxRefundDays) <
            _time.GetUtcNow().UtcDateTime)
        {
            return Failed(
                PaymentFailureKind.Conflict,
                "payment_refund_window_expired",
                "The payment refund window has expired.",
                correlationId,
                rateLimit: rateLimit);
        }

        return new PaymentRefundPreflightResult(
            amountMinorUnits,
            strategy.Operation,
            rateLimit,
            payment,
            provider,
            null);
    }

    private static PaymentRefundPreflightResult Failed(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId,
        Dictionary<string, string[]>? fields = null,
        PaymentRateLimitResult? rateLimit = null) =>
        new(
            0,
            string.Empty,
            rateLimit,
            null,
            null,
            PaymentRefundOperationResult.Failure(
                failureKind,
                errorCode,
                errorMessage,
                correlationId,
                fields,
                rateLimit));
}
