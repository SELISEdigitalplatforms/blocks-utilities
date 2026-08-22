using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
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
internal static class SettlementCharge
{
    public static SubscriptionChargeRequest RequestFor(
        SubscriptionDetail subscription,
        SettlementReservation reservation) => new()
    {
        TenantId = subscription.TenantId,
        // The merchant's scope, not the subscriber's - see BillingAccount.
        OrganizationId = reservation.ProviderOrganizationId ?? subscription.OrganizationId,
        SubscriberOrganizationId = subscription.OrganizationId,
        ProviderName = reservation.ProviderName,
        StoredPaymentMethodId = reservation.StoredPaymentMethodId,
        ProviderCustomerId = reservation.ProviderCustomerId,
        AmountMinor = reservation.ChargeAmountMinor,
        CurrencyCode = subscription.CurrencyCode,
        OrderId = SubscriptionConstants.SettlementOrderIdFor(
            subscription.ItemId,
            reservation.ReservationId),
        Description = Describe(subscription, reservation)
    };

    public static string KeyFor(SubscriptionDetail subscription, SettlementReservation reservation) =>
        SubscriptionConstants.SettlementChargeKeyFor(subscription.ItemId, reservation.ReservationId);

    /// <summary>What the customer sees on their statement, named by what they asked for.</summary>
    private static string Describe(
        SubscriptionDetail subscription,
        SettlementReservation reservation) =>
        reservation.Kind switch
        {
            SettlementReservationKind.PlanChange =>
                $"{reservation.PlanChange?.Plan.DisplayName ?? subscription.Plan.DisplayName} plan change",
            _ => $"{subscription.Plan.DisplayName} quantity change"
        };
}
