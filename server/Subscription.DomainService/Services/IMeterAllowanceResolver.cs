using Subscription.DomainService.Entities;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public interface IMeterAllowanceResolver
{
    /// <summary>
    /// What a window would open with: the plan's allowance plus whatever the window before it left
    /// behind. The figure a window's counter is seeded with.
    /// </summary>
    Task<long> OpeningAllowanceAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        CancellationToken cancellationToken);

    /// <summary>
    /// The allowance in force: the counter's frozen snapshot where the window has opened, and the
    /// opening allowance where it has not.
    /// </summary>
    Task<long> EffectiveAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        SubscriptionUsageCounter? counter,
        CancellationToken cancellationToken);
}
