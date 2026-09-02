using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// How much of a meter a window allows, including anything carried into it.
/// </summary>
/// <remarks>
/// One resolver because two callers must agree: the gate that refuses usage past the allowance, and
/// the entitlement read that advertises it. Computed in two places, they disagreed for exactly as
/// long as a window had no counter — so a plan carrying 80 forward advertised the plan's 100 until
/// the first usage was recorded, and 180 from then on. The advertised limit moved because somebody
/// used the product, which is the one thing an entitlement must never do.
/// </remarks>
public sealed class MeterAllowanceResolver : IMeterAllowanceResolver
{
    private readonly ISubscriptionUsageRepository _usage;

    public MeterAllowanceResolver(ISubscriptionUsageRepository usage) => _usage = usage;

    public async Task<decimal> OpeningAllowanceAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(meter);

        var @base = MeterAllowance.Base(subscription, meter);

        // Every other policy costs nothing: no previous window is read, because none can contribute.
        if (meter.ResetPolicy != MeterResetPolicy.CarryForward)
        {
            return @base;
        }

        return @base + await CarriedIntoAsync(subscription, meter, period, cancellationToken);
    }

    public async Task<decimal> EffectiveAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        SubscriptionUsageCounter? counter,
        CancellationToken cancellationToken)
    {
        // The window's own snapshot wins whenever there is one: it was frozen when the window
        // opened, so nothing computed afterwards may move it. The computation is only the answer
        // for a window that has not opened yet — which is precisely the case the entitlement read
        // used to get wrong.
        if (counter?.LimitSnapshot is { } frozen)
        {
            return frozen;
        }

        return await OpeningAllowanceAsync(subscription, meter, period, cancellationToken);
    }

    /// <summary>
    /// What the previous window leaves to this one, read only for a meter that carries forward.
    /// </summary>
    /// <remarks>
    /// One extra point read. A window that recorded no usage has no counter, which is not an error:
    /// see <see cref="MeterAllowance.CarriedIn"/> for why that carries the plan's quantity rather
    /// than zero.
    /// </remarks>
    private async Task<decimal> CarriedIntoAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        CancellationToken cancellationToken)
    {
        if (!MeterPeriodResolver.TryGetPreviousPeriod(
                subscription,
                meter,
                period,
                out var previousPeriod))
        {
            return 0;
        }

        var previousCounter = await _usage.GetCounterAsync(
            subscription.TenantId,
            SubscriptionUsageCounter.CreateId(
                subscription.ItemId,
                meter.MeterKey,
                previousPeriod.Key),
            cancellationToken);

        return MeterAllowance.CarriedIn(subscription, meter, previousPeriod, previousCounter);
    }
}
