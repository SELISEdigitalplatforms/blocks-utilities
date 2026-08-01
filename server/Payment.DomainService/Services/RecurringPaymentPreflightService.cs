using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class RecurringPaymentPreflightService :
    IRecurringPaymentPreflightService
{
    private readonly IValidator<CreateRecurringPaymentRequest> _validator;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentRateLimiter _rateLimiter;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly IStoredPaymentMethodRepository _storedPaymentMethods;
    private readonly IShopperReferenceService _shopperReferences;

    public RecurringPaymentPreflightService(
        IValidator<CreateRecurringPaymentRequest> validator,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentRateLimiter rateLimiter,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        IStoredPaymentMethodRepository storedPaymentMethods,
        IShopperReferenceService shopperReferences)
    {
        _validator = validator;
        _minorUnits = minorUnits;
        _rateLimiter = rateLimiter;
        _payments = payments;
        _providers = providers;
        _storedPaymentMethods = storedPaymentMethods;
        _shopperReferences = shopperReferences;
    }

    public async Task<RecurringPaymentPreflightResult> ExecuteAsync(
        CreateRecurringPaymentRequest request,
        string idempotencyKey,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var validation =
            await _validator.ValidateAsync(request, cancellationToken);

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

            return Failed(PaymentOperationResult.Failure(
                PaymentFailureKind.Validation,
                "recurring_payment_validation_failed",
                "The recurring payment request is invalid.",
                correlationId,
                fields));
        }

        if (!Guid.TryParse(idempotencyKey, out _) ||
            idempotencyKey.Length > 64)
        {
            return Failed(PaymentOperationResult.Failure(
                PaymentFailureKind.Validation,
                "invalid_idempotency_key",
                "Idempotency-Key must be a UUID with at most 64 characters.",
                correlationId));
        }

        if (!_minorUnits.TryConvert(
                request.Amount,
                request.CurrencyCode.ToUpperInvariant(),
                out var amountMinorUnits))
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
            return Failed(
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Unavailable,
                    "payment_rate_limiter_unavailable",
                    "Payment protection is temporarily unavailable.",
                    correlationId,
                    retryAfterSeconds:
                    rateLimit.RetryAfterSeconds),
                rateLimit);
        }

        if (!rateLimit.IsAllowed)
        {
            return Failed(
                PaymentOperationResult.Failure(
                    PaymentFailureKind.RateLimited,
                    "payment_rate_limit_exceeded",
                    "Too many payment requests.",
                    correlationId,
                    retryAfterSeconds:
                    rateLimit.RetryAfterSeconds,
                    limit: rateLimit.Limit,
                    remaining: rateLimit.Remaining,
                    resetAfterSeconds:
                    rateLimit.ResetAfterSeconds),
                rateLimit);
        }

        // A new payment, so the caller's own organization decides which configuration it uses.
        var provider = await _providers.GetAsync(
            context.TenantId,
            context.OrganizationId,
            request.ProviderName,
            () => _payments.GetProviderAsync(
                context.TenantId,
                context.OrganizationId,
                request.ProviderName,
                cancellationToken));

        if (provider == null ||
            !provider.IsEnabled ||
            string.IsNullOrWhiteSpace(provider.MerchantId) ||
            string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            return Failed(
                PaymentOperationResult.Failure(
                    PaymentFailureKind.NotFound,
                    "payment_provider_not_found",
                    "The payment provider was not found.",
                    correlationId),
                rateLimit);
        }

        if (!_shopperReferences.TryCreate(
                context.TenantId,
                context.ActorId,
                provider.ShopperReferenceHmacKey ?? string.Empty,
                out var shopperReference))
        {
            return Failed(
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Unavailable,
                    "payment_provider_misconfigured",
                    "The payment provider is temporarily unavailable.",
                    correlationId),
                rateLimit);
        }

        var storedPaymentMethod =
            await _storedPaymentMethods.GetAsync(
                context.TenantId,
                request.StoredPaymentMethodId,
                cancellationToken);

        if (storedPaymentMethod == null ||
            !FixedTimeEquals(
                storedPaymentMethod.ShopperReference,
                shopperReference))
        {
            return Failed(
                PaymentOperationResult.Failure(
                    PaymentFailureKind.NotFound,
                    "stored_payment_method_not_found",
                    "The stored payment method was not found.",
                    correlationId),
                rateLimit);
        }

        if (storedPaymentMethod.Status !=
            PaymentMethodStatus.Active)
        {
            return Failed(
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Conflict,
                    "stored_payment_method_unavailable",
                    "The stored payment method is not available for payment.",
                    correlationId),
                rateLimit);
        }

        if (!string.Equals(
                storedPaymentMethod.ProviderName,
                provider.ProviderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Conflict,
                    "stored_payment_method_provider_mismatch",
                    "The stored payment method cannot be used with this provider.",
                    correlationId),
                rateLimit);
        }

        return new RecurringPaymentPreflightResult(
            amountMinorUnits,
            rateLimit,
            provider,
            storedPaymentMethod,
            shopperReference,
            null);
    }

    private static bool FixedTimeEquals(
        string left,
        string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(
                   leftBytes,
                   rightBytes);
    }

    private static RecurringPaymentPreflightResult Failed(
        PaymentOperationResult failure,
        PaymentRateLimitResult? rateLimit = null) =>
        new(0, rateLimit, null, null, null, failure);
}
