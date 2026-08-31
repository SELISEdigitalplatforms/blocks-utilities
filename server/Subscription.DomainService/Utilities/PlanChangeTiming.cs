using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>When a requested plan change takes effect.</summary>
public enum PlanChangeTiming
{
    /// <summary>Applied now, charging whatever the settlement came to.</summary>
    Immediate = 0,

    /// <summary>
    /// Held until the period already paid for runs out, charging nothing today.
    /// </summary>
    NextRenewal = 1
}

/// <summary>
/// Whether a plan change applies now or waits for the paid period to end.
/// </summary>
/// <remarks>
/// Decided by what the change is worth, never by which plan is nominally "higher". A plan's
/// <see cref="Plan.FamilyRank"/> is presentation metadata — it orders a pricing page — and reading
/// it here would let a catalogue's display order decide whether somebody is charged.
/// <para>
/// The rule is the money: a change worth more than what it replaces hands something over now, so
/// it is paid for now. A change worth the same or less takes something away, and taking it away
/// before the subscriber's paid time runs out would be a refund by another name — so it waits.
/// </para>
/// </remarks>
public static class PlanChangeClassifier
{
    /// <param name="settlementMinor">
    /// What the change settles to for the current period, positive when the target is worth more
    /// than what it replaces. This is <see cref="Services.ProrationBreakdown.NetSettlementMinor"/>,
    /// which is the figure <em>before</em> the credit balance pays any of it: whether a change is
    /// an upgrade is a property of the change, not of how much credit happens to be lying around.
    /// A subscriber with credit still gets an immediate upgrade, paid for out of that credit.
    /// </param>
    public static PlanChangeTiming Classify(
        SubscriptionDetail subscription,
        PriceSnapshot targetPrice,
        long settlementMinor)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(targetPrice);

        // A trial has paid nothing, so it has no paid period to protect and nothing to prorate:
        // every trial swap applies at once and charges nothing, which is what it already did
        // before scheduling existed. Classifying it by its settlement would send every one of them
        // to the next renewal, because a trial's settlement is always zero.
        if (subscription.Status == SubscriptionStatus.Trialing)
        {
            return PlanChangeTiming.Immediate;
        }

        // A prepaid year is a commitment that has been settled in full. Re-cadencing it — moving to
        // a monthly price, or to a differently-aligned one — cannot be priced against the year
        // without unpicking a charge that already cleared, so it waits for that year to end
        // regardless of what the arithmetic says this period is worth.
        if (subscription.PendingAnnualPeriod is { IsPrepaid: true } &&
            ChangesCadenceOrAlignment(subscription.Price, targetPrice))
        {
            return PlanChangeTiming.NextRenewal;
        }

        return settlementMinor > 0 ? PlanChangeTiming.Immediate : PlanChangeTiming.NextRenewal;
    }

    /// <summary>
    /// Whether the target bills on a different rhythm than what the subscriber is on.
    /// </summary>
    /// <remarks>
    /// Interval and count together, because "every 3 months" and "every month" are different
    /// rhythms even though both are monthly intervals — and alignment beside them, because a
    /// calendar-aligned month and an anniversary month fall due on different days and a prepaid
    /// year cannot be re-anchored onto the other one mid-commitment.
    /// </remarks>
    private static bool ChangesCadenceOrAlignment(PriceSnapshot current, PriceSnapshot target) =>
        current.Interval != target.Interval ||
        current.IntervalCount != target.IntervalCount ||
        current.BillingAlignment != target.BillingAlignment;
}
