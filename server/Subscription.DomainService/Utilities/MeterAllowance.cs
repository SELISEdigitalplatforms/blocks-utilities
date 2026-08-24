using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// How much of a meter a subscription may use in one window.
/// </summary>
/// <remarks>
/// Pure and static, like the amount and proration calculators, so every rule below can be
/// asserted without a database. Reading the previous window's counter is the caller's job; this
/// only decides what to do with what comes back.
/// </remarks>
public static class MeterAllowance
{
    /// <summary>
    /// The plan's own allowance, before anything is carried into it.
    /// </summary>
    /// <remarks>
    /// A trial's grant replaces the plan's allowance rather than adding to it. Where each unit
    /// costs the seller money, a trial that hands out the full monthly quota is an open invitation
    /// to sign up, consume and leave.
    /// </remarks>
    public static long Base(SubscriptionDetail subscription, PlanMeter meter)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(meter);

        if (subscription.Status != SubscriptionStatus.Trialing ||
            subscription.Trial is null)
        {
            return meter.IncludedQuantity;
        }

        var grant = subscription.Trial.Grants.Find(candidate =>
            string.Equals(candidate.MeterKey, meter.MeterKey, StringComparison.Ordinal));

        return grant?.IncludedQuantity ?? meter.IncludedQuantity;
    }

    /// <summary>
    /// What the window before this one leaves behind for it.
    /// </summary>
    /// <param name="previousCounter">
    /// The previous window's counter, or null when that window recorded no usage at all.
    /// </param>
    /// <remarks>
    /// Four things carry nothing, each for its own reason:
    /// <list type="bullet">
    /// <item>a meter that does not carry forward, and a subscription inside its trial — the grant
    /// is meant to be the whole trial allowance, not a float to bank;</item>
    /// <item>a window that started before the current usage schedule was anchored. That covers the
    /// first window of a subscription, whose predecessor precedes the subscription, and every
    /// window that preceded a plan change, since a change re-anchors the schedule at the change
    /// instant. Carrying across a change would let repeated changes bank allowance;</item>
    /// <item>a window that overlapped the trial, so nothing rolls out of a trial either;</item>
    /// <item>a window that went into overage, which carries zero rather than a negative — the
    /// overage was already invoiced, and letting it reduce the next allowance charges twice.</item>
    /// </list>
    /// <para>
    /// A window with no counter is a window in which nothing was recorded, so its whole allowance
    /// went unused and its whole allowance carries. Measured from the plan rather than from what
    /// that window itself opened with, which is not knowable without a document: an idle window
    /// that had itself carried something in passes on the plan's quantity, not more.
    /// </para>
    /// </remarks>
    public static long CarriedIn(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod previousPeriod,
        SubscriptionUsageCounter? previousCounter)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(meter);

        if (meter.ResetPolicy != MeterResetPolicy.CarryForward ||
            subscription.Status == SubscriptionStatus.Trialing ||
            previousPeriod.StartUtc < subscription.UsageSchedule.AnchorInstantUtc)
        {
            return 0;
        }

        if (subscription.Trial is { } trial &&
            previousPeriod.StartUtc < trial.EndsAtUtc)
        {
            return 0;
        }

        var unused = previousCounter is null
            ? meter.IncludedQuantity
            : (previousCounter.LimitSnapshot ?? meter.IncludedQuantity) - previousCounter.Balance;

        if (unused <= 0)
        {
            return 0;
        }

        return meter.CarryForwardCap is { } cap
            ? Math.Min(unused, Math.Max(0, cap))
            : unused;
    }

    /// <summary>
    /// The allowance a window is actually measured against.
    /// </summary>
    /// <remarks>
    /// The counter's snapshot wins whenever there is one. It is frozen by the insert that opens
    /// the window, so every later call in that window is held to the same number even if the plan
    /// is edited underneath it or the previous window's counter is repaired. The computed value is
    /// only the answer for a window that has not opened yet.
    /// </remarks>
    public static long Effective(SubscriptionUsageCounter? counter, long computed) =>
        counter?.LimitSnapshot ?? computed;
}
