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
                // Marked Utc explicitly: DateTime.MaxValue alone carries Kind.Unspecified, which
                // System.Text.Json serializes with no trailing "Z" — the same instant read back
                // from Mongo (whose driver marks every DateTime Utc on the way out) serializes
                // with one. Two textual forms of one value in the same response is confusing on
                // its own, even though nothing here reads Kind back to decide anything.
                DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
                LifetimePeriodKey);
            return true;
        }

        return BillingPeriodCalculator.TryGetPeriod(
            subscription.UsageSchedule,
            occurredAtUtc,
            out period);
    }

    /// <summary>
    /// The window immediately before <paramref name="period"/>, for a meter that carries unused
    /// allowance forward.
    /// </summary>
    /// <remarks>
    /// Found by asking the schedule which window contains the instant one tick before this one
    /// begins, rather than by subtracting an interval: month lengths and daylight saving make
    /// boundary arithmetic wrong twice a year, and the calculator already knows how to place an
    /// instant. False for any other policy, which has no predecessor to consult.
    /// </remarks>
    public static bool TryGetPreviousPeriod(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        out BillingPeriod previous)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(meter);

        previous = default;

        return meter.ResetPolicy == MeterResetPolicy.CarryForward &&
               BillingPeriodCalculator.TryGetPeriod(
                   subscription.UsageSchedule,
                   period.StartUtc.AddTicks(-1),
                   out previous);
    }
}
