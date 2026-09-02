using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Freezes every resetting meter's effective allowance for a <see cref="PendingUsagePeriod"/>
/// at the moment it is captured — before the status/schedule transition (cancellation or plan
/// change) that cuts the window short takes effect.
/// </summary>
/// <remarks>
/// Without this, final rating of a cut-short window has to fall back to resolving the allowance
/// against whatever the subscription looks like by the time the rating sweep runs — which is
/// after the transition, on the wrong side of it. A cancellation has already moved status to
/// <see cref="SubscriptionStatus.Canceled"/> and a plan change has already re-anchored
/// <see cref="Entities.BillingSchedule"/> by then, so a trial grant or a carried-forward
/// allowance the outgoing window actually opened with would be lost, and the subscriber overbilled
/// for usage that should have been covered. See <see cref="IMeterAllowanceResolver"/> for how a
/// window's counter, once one exists, freezes the same figure on its own — this exists only to
/// cover the crash window before that counter is written.
/// </remarks>
public static class MeterAllowanceSnapshot
{
    /// <summary>
    /// Resolves the effective allowance of every resetting meter on <paramref name="subscription"/>'s
    /// plan for <paramref name="period"/>, using whatever counter already exists for it.
    /// </summary>
    /// <returns>
    /// Null when <paramref name="usage"/> or <paramref name="allowances"/> was not supplied — the
    /// caller then queues a <see cref="PendingUsagePeriod"/> with no snapshot, and final rating
    /// falls back to resolving live for it, exactly as it did before this snapshot existed.
    /// </returns>
    public static async Task<Dictionary<string, decimal>?> CaptureAsync(
        SubscriptionDetail subscription,
        BillingPeriod period,
        ISubscriptionUsageRepository? usage,
        IMeterAllowanceResolver? allowances,
        CancellationToken cancellationToken)
    {
        if (usage is null || allowances is null)
        {
            return null;
        }

        var counters = (await usage.ListCountersAsync(
                subscription.TenantId,
                subscription.ItemId,
                period.Key,
                cancellationToken))
            .ToDictionary(counter => counter.MeterKey, StringComparer.Ordinal);

        var snapshot = new Dictionary<string, decimal>(StringComparer.Ordinal);

        // Every resetting meter, matching exactly the set SubscriptionUsageRatingProcessor rates
        // for a closed window — Never sits outside per-period rating entirely and never needs an
        // allowance snapshotted here either.
        foreach (var meter in subscription.Plan.Meters.Where(
                     planMeter => planMeter.ResetPolicy != MeterResetPolicy.Never))
        {
            var counter = counters.GetValueOrDefault(meter.MeterKey);
            snapshot[meter.MeterKey] = await allowances.EffectiveAsync(
                subscription, meter, period, counter, cancellationToken);
        }

        return snapshot;
    }
}
