using FluentValidation;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class PaymentPreflightService : IPaymentPreflightService
{
    private readonly IValidator<MakePaymentRequest> _validator;
    private readonly ICurrencyMinorUnitResolver _currencyResolver;
    private readonly IPaymentRateLimiter _rateLimiter;

    public PaymentPreflightService(
        IValidator<MakePaymentRequest> validator,
        ICurrencyMinorUnitResolver currencyResolver,
        IPaymentRateLimiter rateLimiter)
    {
        _validator = validator;
        _currencyResolver = currencyResolver;
        _rateLimiter = rateLimiter;
    }

    public async Task<PaymentPreflightResult> ExecuteAsync(
        MakePaymentRequest request,
        string idempotencyKey,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (request.HasConflictingSavePaymentPreferences)
        {
            return Failed(PaymentOperationResult.Failure(
                PaymentFailureKind.Validation,
                "conflicting_save_payment_preferences",
                "SavePaymentMethod and RememberCard must have the same value.",
                correlationId,
                new Dictionary<string, string[]>
                {
                    [nameof(MakePaymentRequest.SavePaymentMethod)] =
                    [
                        "SavePaymentMethod and RememberCard must have the same value."
                    ]
                }));
        }

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var fields = validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

            return Failed(PaymentOperationResult.Failure(
                PaymentFailureKind.Validation,
                "payment_validation_failed",
                "The payment request is invalid.",
                correlationId,
                fields));
        }

        if (!Guid.TryParse(idempotencyKey, out _) || idempotencyKey.Length > 64)
        {
            return Failed(PaymentOperationResult.Failure(
                PaymentFailureKind.Validation,
                "invalid_idempotency_key",
                "Idempotency-Key must be a UUID with at most 64 characters.",
                correlationId));
        }

        if (!_currencyResolver.TryConvert(request.Amount, request.CurrencyCode.ToUpperInvariant(), out var minorUnits))
        {
            return Failed(PaymentOperationResult.Failure(
                PaymentFailureKind.Validation,
                "unsupported_currency_or_precision",
                "The currency is unsupported or the amount has invalid precision.",
                correlationId));
        }

        var rateLimit = await _rateLimiter.CheckAsync(
            context.TenantId,
            context.ActorId,
            request.OrderId,
            cancellationToken);

        if (!rateLimit.IsAvailable)
        {
            return new PaymentPreflightResult(
                0,
                rateLimit,
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Unavailable,
                    "payment_rate_limiter_unavailable",
                    "Payment protection is temporarily unavailable.",
                    correlationId,
                    retryAfterSeconds: rateLimit.RetryAfterSeconds));
        }

        if (!rateLimit.IsAllowed)
        {
            return new PaymentPreflightResult(
                0,
                rateLimit,
                PaymentOperationResult.Failure(
                    PaymentFailureKind.RateLimited,
                    "payment_rate_limit_exceeded",
                    "Too many payment requests.",
                    correlationId,
                    retryAfterSeconds: rateLimit.RetryAfterSeconds,
                    limit: rateLimit.Limit,
                    remaining: rateLimit.Remaining,
                    resetAfterSeconds: rateLimit.ResetAfterSeconds));
        }

        return new PaymentPreflightResult(minorUnits, rateLimit, null);
    }

    private static PaymentPreflightResult Failed(PaymentOperationResult failure) => new(0, null, failure);
}
