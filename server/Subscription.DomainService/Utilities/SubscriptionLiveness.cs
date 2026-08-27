using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Whether a subscription currently grants access — the one rule every "is this live" check
/// shares, so it cannot drift between the read that answers <c>/current</c>, the read that
/// answers an entitlement check, and the read that admits a usage record.
/// </summary>
/// <remarks>
/// A scheduled cancellation (<see cref="SubscriptionDetail.CancelAtPeriodEnd"/>) stops granting
/// the instant its promised <see cref="SubscriptionDetail.CurrentPeriodEndUtc"/> passes, whether
/// or not the finalizing worker has run yet. Treating <c>Status</c> alone as the answer — the
/// older, narrower rule — lets a subscription go on granting access, and accruing billable usage,
/// for however long the worker happened to be delayed.
/// </remarks>
public static class SubscriptionLiveness
{
    /// <summary>Statuses that grant something, independent of any cancellation schedule.</summary>
    public static readonly SubscriptionStatus[] LiveStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
        SubscriptionStatus.PastDue
    ];

    public static bool IsEffectivelyLive(SubscriptionDetail subscription, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return LiveStatuses.Contains(subscription.Status)
            && !(subscription.CancelAtPeriodEnd && subscription.CurrentPeriodEndUtc <= nowUtc);
    }
}
