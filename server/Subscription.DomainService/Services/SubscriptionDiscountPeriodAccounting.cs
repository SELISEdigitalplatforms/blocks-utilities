using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>Shared rules for projecting and recording promotional discount-period consumption.</summary>
internal static class SubscriptionDiscountPeriodAccounting
{
    /// <summary>Whether the confirmed opening charge consumes one promotional period.</summary>
    /// <remarks>
    /// Read from what checkout froze, never recalculated. Stub or whole period counts for a
    /// calendar-aligned price; anniversary pricing retains its established behavior of not
    /// consuming the opening period. A first-annual campaign consumes its period only when the
    /// opening payment also collected the annual term, or no separate annual term exists.
    /// </remarks>
    internal static bool OpeningChargeSpentPeriod(SubscriptionDetail subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (!subscription.InitialChargeDiscountApplied ||
            !CalendarBillingAlignment.IsCalendarAligned(subscription.Price))
        {
            return false;
        }

        if (subscription.Discount?.Campaign.Kind == CampaignKind.FirstAnnualPeriod)
        {
            return subscription.PendingAnnualPeriod is null ||
                   subscription.PendingAnnualPeriod.CollectedWithCheckout;
        }

        return true;
    }
}
