using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class Discount
{
    [BsonId] public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public CatalogueStatus Status { get; set; } = CatalogueStatus.Active;
    public DiscountTerms Terms { get; set; } = new();
    public string? CurrencyCode { get; set; }
    public List<string> ApplicablePlanCodes { get; set; } = [];

    /// <summary>
    /// The prices this code may be used on. Empty is unrestricted by price, which is what every
    /// discount stored before this existed carries.
    /// </summary>
    /// <remarks>
    /// Price identifiers rather than cadences, because a plan can sell two yearly prices in two
    /// currencies and a promotion often means exactly one of them. Combined with
    /// <see cref="ApplicablePlanCodes"/> by <em>and</em>: naming both narrows twice rather than
    /// offering two ways to qualify.
    /// </remarks>
    public List<string> ApplicablePriceIds { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Incremented on every successful edit. An update request must name the value it read, and a
    /// mismatch is refused with <c>subscription_discount_version_conflict</c> rather than applied
    /// over an edit the caller never saw -- the same reason a stale write to any shared record is
    /// worth refusing rather than silently winning.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Campaign and discount-composition behaviour layered on top of the ordinary percentage or
    /// fixed reduction above. Always present, never null. A discount created before campaigns
    /// existed, or one a caller creates without naming a kind, deserializes with
    /// <see cref="CampaignTerms.Kind"/> at its zero value, <see cref="CampaignKind.Standard"/>,
    /// which every campaign-specific code path treats identically to "no campaign at all".
    /// </summary>
    public CampaignTerms Campaign { get; set; } = new();
}

/// <summary>
/// The campaign-specific rules a discount carries beyond <see cref="DiscountTerms.Kind"/>'s plain
/// percentage or fixed reduction: which billing periods it may touch, how it interacts with a
/// price's own automatic and volume discounts, when it may be redeemed, and what else changes
/// alongside the reduction.
/// </summary>
/// <remarks>
/// One embedded document rather than a dozen fields flattened onto <see cref="Discount"/>, so
/// campaign-specific code paths have one embedded source of truth. Standard discounts use only the
/// precedence fields; campaign windows, redemption rules and entitlement overrides remain gated on
/// <c>discount.Campaign.Kind != CampaignKind.Standard</c>.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class CampaignTerms
{
    public CampaignKind Kind { get; set; } = CampaignKind.Standard;
    public CampaignPrecedence Precedence { get; set; } = CampaignPrecedence.BestDiscount;

    /// <summary>
    /// Whether a Standard discount explicitly chose how it meets built-in discounts.
    /// </summary>
    /// <remarks>
    /// False is the backward-compatible value for every Standard discount stored before this
    /// capability existed: it continues through the plan's QuantityDiscountCombinationPolicy.
    /// Campaign kinds always use <see cref="Precedence"/> and do not depend on this marker.
    /// </remarks>
    public bool PrecedenceConfigured { get; set; }

    /// <summary>
    /// The campaign's own validity window, as the admin who authored it typed it -- calendar
    /// dates, with no time of day, interpreted in <see cref="TimeZoneId"/>. Null for a
    /// <see cref="CampaignKind.Standard"/> discount, which has no window of its own and is instead
    /// governed by the legacy <see cref="DiscountTerms.ExpiresAtUtc"/>.
    /// </summary>
    public DateOnly? ValidFromDate { get; set; }

    /// <summary>
    /// Inclusive: the last calendar date the campaign may still be redeemed on, in
    /// <see cref="TimeZoneId"/>. Converted at authoring time into the exclusive
    /// <see cref="RedeemableUntilUtc"/> that redemption actually checks against -- never
    /// re-derived at redemption time, so a time-zone rule change between authoring and redemption
    /// cannot move a boundary a subscriber was already shown.
    /// </summary>
    public DateOnly? ValidThroughDate { get; set; }

    /// <summary>The IANA zone <see cref="ValidFromDate"/> and <see cref="ValidThroughDate"/> are read in.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// <see cref="ValidFromDate"/> converted to an instant, computed and persisted at authoring
    /// time rather than derived on every redemption check.
    /// </summary>
    public DateTime? RedeemableFromUtc { get; set; }

    /// <summary>
    /// The instant after which the campaign may no longer be redeemed -- exclusive, and the local
    /// midnight that begins the day <em>after</em> <see cref="ValidThroughDate"/>, so the whole of
    /// the inclusive end date is still redeemable.
    /// </summary>
    public DateTime? RedeemableUntilUtc { get; set; }

    /// <summary>
    /// Whether an organization may redeem this campaign more than once. Enforced by a unique index
    /// on the redemption ledger, not by this flag alone -- this only decides whether the reservation
    /// attempt is made under a scope that can collide.
    /// </summary>
    public bool OneUsePerOrganization { get; set; }

    /// <summary>
    /// Whether activating a subscription under this campaign requires a stored payment method,
    /// even on a plan that does not otherwise require one upfront. A campaign that sets this
    /// overrides the plan's own <c>RequirePaymentMethodUpfront</c> for the duration it applies --
    /// never the reverse, since a plan requiring a card cannot be waived by a campaign that says
    /// nothing about it.
    /// </summary>
    public bool RequiresPaymentMethodUpfront { get; set; }

    /// <summary>
    /// For <see cref="CampaignKind.FreeOpeningCalendarPeriod"/>: the one count entitlement this
    /// campaign temporarily caps, and to what. Null for every other kind.
    /// </summary>
    public CampaignEntitlementOverride? EntitlementOverride { get; set; }
}
