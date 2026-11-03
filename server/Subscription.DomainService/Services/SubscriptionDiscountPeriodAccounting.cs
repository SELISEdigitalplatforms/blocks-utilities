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

        if (StubTakesCode(subscription.Discount))
        {
            return subscription.PendingAnnualPeriod is null ||
                   subscription.PendingAnnualPeriod.CollectedWithCheckout;
        }

        return true;
    }

    /// <summary>
    /// Whether a calendar-aligned yearly signup's opening stub is priced with the subscriber's code,
    /// rather than leaving it to the year that follows.
    /// </summary>
    /// <remarks>
    /// An ordinary code belongs to the year. Two kinds reach back to the stub: a FirstAnnualPeriod
    /// campaign, whose offer covers both by definition, and a code set to replace the price's own
    /// discount. Priced without it, that code's stub still took the automatic discount, so a
    /// subscriber who bought "50% instead of 8%" was invoiced the 8% on the stub and the 50% on the
    /// year — two discounts where the code promised one.
    /// <para>
    /// Both are accounted like a FirstAnnualPeriod campaign in <see cref="OpeningChargeSpentPeriod"/>:
    /// a stub and the year it opens spend one promotional period between them, not one each.
    /// </para>
    /// </remarks>
    internal static bool StubTakesCode(DiscountTerms? discount) =>
        discount?.Campaign is { } campaign &&
        (campaign.Kind == CampaignKind.FirstAnnualPeriod ||
         (campaign.Kind == CampaignKind.Standard &&
          campaign.PrecedenceConfigured &&
          campaign.Precedence == CampaignPrecedence.ReplaceBuiltIn));
}
