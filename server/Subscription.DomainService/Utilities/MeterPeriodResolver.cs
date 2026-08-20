using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>Chooses the counter address for a meter from its reset policy.</summary>
public static class MeterPeriodResolver
{
    public const string LifetimePeriodKey = "LIFETIME";

    public static bool TryGetPeriod(
        SubscriptionDetail subscription,
        PlanMeter meter,
        DateTime occurredAtUtc,
        out BillingPeriod period)
    {
        if (meter.ResetPolicy == MeterResetPolicy.Never)
        {
            period = new BillingPeriod(
                0,
                subscription.CreatedAtUtc,
                DateTime.MaxValue,
                LifetimePeriodKey);
            return true;
        }

        return BillingPeriodCalculator.TryGetPeriod(
            subscription.UsageSchedule,
            occurredAtUtc,
            out period);
    }
}
