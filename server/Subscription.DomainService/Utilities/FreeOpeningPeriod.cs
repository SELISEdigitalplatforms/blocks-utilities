using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Whether a free-opening-period campaign is still running on a subscription -- the one rule the
/// plan-change lock, the quantity-change lock and the temporary entitlement cap share, so the
/// offer cannot end for one of them and keep going for another.
/// </summary>
/// <remarks>
/// The campaign's discount stays on the subscription after its one free period is spent: it is
/// the record of what was redeemed, and <see cref="DiscountTerms.DurationPeriods"/> is what stops
/// it pricing a second period. So "a FreeOpeningCalendarPeriod discount is attached and now is
/// before CurrentPeriodEndUtc" is not enough on its own -- after the first renewal
/// CurrentPeriodEndUtc is the end of a paid month, and that check kept the subscriber locked out
/// of plan and quantity changes (and capped by the campaign's entitlement) forever.
/// </remarks>
public static class FreeOpeningPeriod
{
    public static bool IsInForce(SubscriptionDetail subscription, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (subscription.Discount is not { Campaign.Kind: CampaignKind.FreeOpeningCalendarPeriod } discount ||
            nowUtc >= subscription.CurrentPeriodEndUtc)
        {
            return false;
        }

        // Still the period the code was redeemed in. Renewal opens the next period on a later
        // boundary than the signup instant, so this turns false at the first renewal and stays
        // false. RedeemedAtUtc shipped with campaigns themselves, so null is never expected here;
        // it keeps the lock rather than lifting a promise early.
        return discount.RedeemedAtUtc is not { } redeemedAt ||
               redeemedAt >= subscription.CurrentPeriodStartUtc;
    }
}
