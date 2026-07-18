using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class StoredPaymentMethodQueryService :
    IStoredPaymentMethodQueryService
{
    private readonly IPaymentExecutionContextResolver _contexts;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly IShopperReferenceService _shopperReferences;
    private readonly IStoredPaymentMethodRepository _methods;
    private readonly IStoredPaymentMethodRateLimiter _rateLimiter;

    public StoredPaymentMethodQueryService(
        IPaymentExecutionContextResolver contexts,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        IShopperReferenceService shopperReferences,
        IStoredPaymentMethodRepository methods,
        IStoredPaymentMethodRateLimiter rateLimiter)
    {
        _contexts = contexts;
        _payments = payments;
        _providers = providers;
        _shopperReferences = shopperReferences;
        _methods = methods;
        _rateLimiter = rateLimiter;
    }

    public async Task<StoredPaymentMethodQueryResult>
        GetStoredPaymentMethodsAsync(
            string correlationId,
            CancellationToken cancellationToken)
    {
        var resolved = _contexts.Resolve(correlationId);

        if (!resolved.IsSuccess)
        {
            return Failure(resolved.Failure!);
        }

        var context = resolved.Context!;
        var rateLimit = await _rateLimiter.CheckListAsync(
            context.TenantId,
            context.ActorId,
            cancellationToken);

        if (!rateLimit.IsAvailable)
        {
            return Unavailable(
                "payment_method_rate_limiter_unavailable",
                "Payment method protection is temporarily unavailable.",
                rateLimit);
        }

        if (!rateLimit.IsAllowed)
        {
            return new StoredPaymentMethodQueryResult(
                false,
                null,
                PaymentFailureKind.RateLimited,
                "payment_method_rate_limit_exceeded",
                "Too many stored payment method requests.",
                rateLimit);
        }

        var provider = await GetProviderAsync(
            context.TenantId,
            cancellationToken);

        if (provider == null ||
            !provider.IsEnabled ||
            !_shopperReferences.TryCreate(
                context.TenantId,
                context.ActorId,
                provider.ShopperReferenceHmacKey ?? string.Empty,
                out var shopperReference))
        {
            return Unavailable(
                "payment_provider_unavailable",
                "The payment provider is temporarily unavailable.",
                rateLimit);
        }

        var methods = await _methods.ListActiveAsync(
            context.TenantId,
            shopperReference,
            cancellationToken);

        return new StoredPaymentMethodQueryResult(
            true,
            methods.Select(Map).ToArray(),
            PaymentFailureKind.None,
            string.Empty,
            string.Empty,
            rateLimit);
    }

    private Task<PaymentProvider?> GetProviderAsync(
        string tenantId,
        CancellationToken cancellationToken) =>
        _providers.GetAsync(
            tenantId,
            PaymentConstants.AdyenOnlineProvider,
            () => _payments.GetProviderAsync(
                tenantId,
                PaymentConstants.AdyenOnlineProvider,
                cancellationToken));

    private static StoredPaymentMethodResponse Map(
        StoredPaymentMethod method) =>
        new()
        {
            PaymentMethodId = method.ItemId,
            Type = method.Type,
            Brand = method.Brand,
            LastFour = method.LastFour,
            ExpiryMonth = method.ExpiryMonth,
            ExpiryYear = method.ExpiryYear,
            FundingSource = method.FundingSource,
            IssuerCountry = method.IssuerCountry,
            Status = "ACTIVE"
        };

    private static StoredPaymentMethodQueryResult Failure(
        PaymentOperationResult result) =>
        new(
            false,
            null,
            result.FailureKind,
            result.ErrorCode,
            result.ErrorMessage);

    private static StoredPaymentMethodQueryResult Unavailable(
        string code,
        string message,
        PaymentRateLimitResult? rateLimit = null) =>
        new(
            false,
            null,
            PaymentFailureKind.Unavailable,
            code,
            message,
            rateLimit);
}
