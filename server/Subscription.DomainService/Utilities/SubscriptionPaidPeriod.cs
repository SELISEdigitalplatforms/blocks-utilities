using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// The instant a subscription's already-paid time actually runs out.
/// </summary>
/// <remarks>
/// The one date a change that is not refunded may take effect on. A decrease, a downgrade and a
/// cadence change all take something away, and taking it away before this instant would be a
/// refund the subscriber never agreed to.
/// <para>
/// Normally the current period's own end. The exception is a calendar-aligned yearly subscription
/// still inside its opening stub with the year <em>already paid</em>: there,
/// <see cref="SubscriptionDetail.CurrentPeriodEndUtc"/> is the upcoming first of the month — the
/// end of the stub, not of the year — and scheduling against it would take seats or a plan away
/// about a month after signup, in the middle of an annual commitment settled in full.
/// </para>
/// <para>
/// An <em>unpaid</em> opening stub is deliberately not the exception: nothing beyond the stub has
/// been bought, so the stub's end really is the end of what was paid for. The prepaid year's end
/// is also only preferred when it is genuinely later, so a subscription whose year has already
/// opened is never pushed backwards by a stale record.
/// </para>
/// </remarks>
public static class SubscriptionPaidPeriod
{
    public static DateTime PaidThroughUtc(SubscriptionDetail subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return subscription.PendingAnnualPeriod is { IsPrepaid: true } prepaid &&
            prepaid.EndUtc > subscription.CurrentPeriodEndUtc
                ? prepaid.EndUtc
                : subscription.CurrentPeriodEndUtc;
    }
}
