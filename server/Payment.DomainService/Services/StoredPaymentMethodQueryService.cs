using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers;
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
    private readonly IPaymentProviderCatalog _catalog;
    private readonly IShopperReferenceService _shopperReferences;
    private readonly IStoredPaymentMethodRepository _methods;
    private readonly IStoredPaymentMethodRateLimiter _rateLimiter;

    public StoredPaymentMethodQueryService(
        IPaymentExecutionContextResolver contexts,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        IPaymentProviderCatalog catalog,
        IShopperReferenceService shopperReferences,
        IStoredPaymentMethodRepository methods,
        IStoredPaymentMethodRateLimiter rateLimiter)
    {
        _contexts = contexts;
        _payments = payments;
        _providers = providers;
        _catalog = catalog;
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

        var shopperReferences = await ResolveShopperReferencesAsync(
            context,
            cancellationToken);

        if (shopperReferences.Count == 0)
        {
            return Unavailable(
                "payment_provider_unavailable",
                "The payment provider is temporarily unavailable.",
                rateLimit);
        }

        var methods = await _methods.ListActiveAsync(
            context.TenantId,
            shopperReferences,
            cancellationToken);

        return new StoredPaymentMethodQueryResult(
            true,
            methods.Select(Map).ToArray(),
            PaymentFailureKind.None,
            string.Empty,
            string.Empty,
            rateLimit);
    }

    /// <summary>
    /// One shopper reference per enabled provider the tenant has registered.
    /// </summary>
    /// <remarks>
    /// The reference is an HMAC under the provider's own key, so each provider yields a
    /// different one for the same shopper and a card is only findable under the reference of
    /// the provider that stored it. Deriving from a single hard-coded provider hid every card
    /// saved at any other one.
    /// </remarks>
    private async Task<IReadOnlyCollection<string>> ResolveShopperReferencesAsync(
        PaymentExecutionContext context,
        CancellationToken cancellationToken)
    {
        var references = new List<string>(_catalog.RegisteredProviderNames.Count);

        // Driven by the catalog rather than by querying the provider documents, so this asks
        // for each provider by name exactly as the single-provider lookup always did. Listing
        // the documents instead would filter on their TenantId field, which the per-tenant
        // lookup never relied on, and would hide any provider whose field disagrees.
        foreach (var name in _catalog.RegisteredProviderNames)
        {
            // Through the cache, which is what decrypts the key the reference is derived from.
            var provider = await _providers.GetAsync(
                context.TenantId,
                context.OrganizationId,
                name,
                () => _payments.GetProviderAsync(
                    context.TenantId,
                    context.OrganizationId,
                    name,
                    cancellationToken));

            if (provider is { IsEnabled: true } &&
                _shopperReferences.TryCreate(
                    context.TenantId,
                    context.ActorId,
                    provider.ShopperReferenceHmacKey ?? string.Empty,
                    out var shopperReference))
            {
                references.Add(shopperReference);
            }
        }

        return references;
    }

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
