using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// The one place a <see cref="SubscriptionDetail"/> becomes a <see cref="SubscriptionResponse"/>
/// with its billing account's own facts attached.
/// </summary>
/// <remarks>
/// <see cref="ISubscriptionResponseMapper.ToResponse"/> takes <c>providerName</c> and
/// <c>hasPaymentMethod</c> as plain, independently-optional parameters -- which let a caller
/// supply one and forget the other, and several did: <c>GetCurrentAsync</c>'s incomplete-checkout
/// branch, the pending-checkout resume path, and a card-free trial's activation response all
/// mapped a subscription without ever naming its provider, so <c>ProviderName</c> came back null
/// on exactly the paths a client most needs it -- the ones with no checkout URL to infer a
/// provider from another way.
/// <para>
/// Both facts come from the same billing account, so this fetches it once and derives both
/// together -- there is no longer a way to supply one and silently omit the other. Every
/// production caller that maps a subscription into a response should go through this rather than
/// calling <see cref="ISubscriptionResponseMapper.ToResponse"/> directly, unless it already holds
/// the billing account and can pass it straight to the mapper's own <c>providerName</c>/
/// <c>hasPaymentMethod</c> parameters itself (as <c>ChargeAsync</c> and <c>StartCardSetupAsync</c>
/// do, having just fetched the account to resolve the provider to charge through).
/// </para>
/// </remarks>
public static class SubscriptionResponseMapperExtensions
{
    public static async Task<SubscriptionResponse> ToResponseAsync(
        this ISubscriptionResponseMapper mapper,
        IBillingAccountRepository billingAccounts,
        SubscriptionDetail subscription,
        string? checkoutUrl,
        PendingCheckoutResponse? pendingCheckout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(billingAccounts);
        ArgumentNullException.ThrowIfNull(subscription);

        var account = await billingAccounts.GetAsync(
            subscription.TenantId, subscription.BillingAccountId, cancellationToken);

        return mapper.ToResponse(
            subscription,
            checkoutUrl,
            pendingCheckout,
            hasPaymentMethod: account?.DefaultPaymentMethodId is { Length: > 0 },
            providerName: account?.ProviderName);
    }
}
