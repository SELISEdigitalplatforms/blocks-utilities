using Payment.DomainService.Entities;
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
            reservation.Kind,
            reservation.ReservationId),
        Description = Describe(subscription, reservation),
        // From the reservation, which recorded it when the change was quoted — not from the
        // subscription as it stands now, which the settlement is about to change.
        Settlement = reservation.Settlement
    };

    /// <summary>
    /// The proration's own arithmetic in the shape a payment record stores.
    /// </summary>
    /// <remarks>
    /// Here rather than at each reservation site, so a plan change and a quantity change cannot
    /// describe the same kind of charge differently. Returns null for an outcome that prorated
    /// nothing — a malformed period, where there are no two sides to report.
    /// </remarks>
    public static SubscriptionSettlementBreakdown? BreakdownOf(ProrationOutcome outcome)
    {
        var breakdown = outcome.Breakdown;

        if (breakdown == default)
        {
            return null;
        }

        return new SubscriptionSettlementBreakdown
        {
            Outgoing = SideOf(breakdown.Outgoing),
            Target = SideOf(breakdown.Target),
            CreditConsumedMinor = breakdown.CreditConsumedMinor,
            NetSettlementMinor = breakdown.NetSettlementMinor
        };
    }

    /// <summary>
    /// The composite arithmetic of an opening-stub upgrade in the shape a payment record stores:
    /// the stub at top level, the prepaid year nested beneath it.
    /// </summary>
    /// <remarks>
    /// Only the top-level <see cref="SubscriptionSettlementBreakdown.CreditConsumedMinor"/> and
    /// <see cref="SubscriptionSettlementBreakdown.NetSettlementMinor"/> are the figures actually
    /// charged — see <see cref="OpeningStubUpgradeOutcome"/> — the nested <c>Annual</c> breakdown
    /// carries the annual side's own raw figures purely for the invoice to explain.
    /// </remarks>
    public static SubscriptionSettlementBreakdown BreakdownOf(OpeningStubUpgradeOutcome outcome) => new()
    {
        Outgoing = SideOf(outcome.Stub.Outgoing),
        Target = SideOf(outcome.Stub.Target),
        CreditConsumedMinor = outcome.CreditConsumedMinor,
        NetSettlementMinor = outcome.NetSettlementMinor,
        Annual = new SubscriptionSettlementBreakdown
        {
            Outgoing = SideOf(outcome.Annual.Outgoing),
            Target = SideOf(outcome.Annual.Target),
            CreditConsumedMinor = 0,
            NetSettlementMinor = outcome.Annual.NetSettlementMinor
        }
    };

    private static SubscriptionSettlementSide SideOf(ProrationSide side) => new()
    {
        GrossAmountMinor = side.GrossAmountMinor,
        BuiltInDiscountMinor = side.BuiltInDiscountMinor,
        PromotionalDiscountMinor = side.PromotionalDiscountMinor,
        TaxAmountMinor = side.TaxAmountMinor,
        PeriodTotalMinor = side.PeriodTotalMinor,
        ProratedValueMinor = side.ProratedValueMinor
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
