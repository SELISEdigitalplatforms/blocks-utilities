using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>
/// What kind of promotion a discount is layered onto a plan and price as, beyond the ordinary
/// percentage or fixed reduction <see cref="DiscountKind"/> already describes.
/// </summary>
/// <remarks>
/// Zero, <see cref="Standard"/>, is what every discount authored before this existed deserializes
/// to — same convention as <see cref="AutomaticDiscountCombination.BestDiscount"/> being zero: the
/// unset value has to be the one that changes nothing, because a document nobody meant to touch
/// still deserializes through this field.
/// <para>
/// A discount's <see cref="DiscountKind"/> says how much it takes off; this says which billing
/// periods it is allowed to touch and what else happens alongside the reduction. The two are
/// independent axes, the same way <see cref="BillingAlignment"/> is independent of
/// <see cref="BillingInterval"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CampaignKind
{
    /// <summary>
    /// An ordinary discount: no campaign date window of its own, no redemption ledger entry, no
    /// entitlement override. Everything before campaigns existed, and everything created without
    /// naming one of the kinds below.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Discounts a calendar-aligned yearly price's opening stub and its first full annual period,
    /// and only those. A renewal past the first annual period reverts to the price's own
    /// automatic discount.
    /// </summary>
    FirstAnnualPeriod = 1,

    /// <summary>
    /// A 100% reduction on a calendar-aligned monthly price's opening period only — from signup to
    /// the next local calendar-month boundary — paired with a temporary entitlement override.
    /// </summary>
    FreeOpeningCalendarPeriod = 2
}
