using Subscription.DomainService.Entities;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public interface IMeterAllowanceResolver
{
    /// <summary>
    /// What a window would open with: the plan's allowance plus whatever the window before it left
    /// behind. The figure a window's counter is seeded with.
    /// </summary>
    /// <param name="seat">
    /// Which seat's window, or null for the subscription's own. A carried-forward allowance must
    /// come from the same seat's previous window — read from the subscription's it would hand one
    /// person whatever another had left over, and on a plan where everyone carries forward it
    /// would hand all of them the same leftovers.
    /// </param>
    Task<decimal> OpeningAllowanceAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        CancellationToken cancellationToken,
        int? seat = null);

    /// <summary>
    /// The allowance in force: the counter's frozen snapshot where the window has opened, and the
    /// opening allowance where it has not.
    /// </summary>
    Task<decimal> EffectiveAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        SubscriptionUsageCounter? counter,
        CancellationToken cancellationToken,
        int? seat = null);
}
