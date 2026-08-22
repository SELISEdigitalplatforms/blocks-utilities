using Subscription.DomainService.Entities;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// The charge a quantity reservation raises.
/// </summary>
/// <remarks>
/// One place because two callers raise the same charge: the request that took the reservation, and
/// the sweep that replays it when nobody recorded what happened. Built from the reservation rather
/// than from the billing account, so both spell the same attempt — including its idempotency key,
/// which is the only reason a replay finds the charge already raised instead of raising a second.
/// </remarks>
internal static class QuantityChangeCharge
{
    public static SubscriptionChargeRequest RequestFor(
        SubscriptionDetail subscription,
        QuantityChangeClaim claim) => new()
    {
        TenantId = subscription.TenantId,
        // The merchant's scope, not the subscriber's - see BillingAccount.
        OrganizationId = claim.ProviderOrganizationId ?? subscription.OrganizationId,
        SubscriberOrganizationId = subscription.OrganizationId,
        ProviderName = claim.ProviderName,
        StoredPaymentMethodId = claim.StoredPaymentMethodId,
        ProviderCustomerId = claim.ProviderCustomerId,
        AmountMinor = claim.ChargeAmountMinor,
        CurrencyCode = subscription.CurrencyCode,
        OrderId = SubscriptionConstants.QuantityChangeOrderIdFor(
            subscription.ItemId,
            claim.ClaimId),
        Description = $"{subscription.Plan.DisplayName} quantity change"
    };

    public static string KeyFor(SubscriptionDetail subscription, QuantityChangeClaim claim) =>
        SubscriptionConstants.QuantityChangeKeyFor(subscription.ItemId, claim.ClaimId);
}
