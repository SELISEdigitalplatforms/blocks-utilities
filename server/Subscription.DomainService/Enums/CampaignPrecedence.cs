using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>
/// How a campaign's reduction interacts with a price's own automatic and volume discounts.
/// </summary>
/// <remarks>
/// Zero is <see cref="BestDiscount"/>, the same conservative default
/// <see cref="AutomaticDiscountCombination"/> already uses for the same reason: a campaign
/// document that predates this field, or one authored by a caller that never named it, must read
/// back as the answer that can never overcharge <em>or</em> silently discount more than either
/// side individually promised. Authoring a campaign with <see cref="ReplaceBuiltIn"/> is a
/// deliberate choice made when that campaign is created — it is not what an absent value means.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CampaignPrecedence
{
    /// <summary>
    /// Whichever single reduction is larger — the campaign's, or the price's own automatic and
    /// volume discounts combined under its own <see cref="AutomaticDiscountCombination"/> — never
    /// both. The conservative default.
    /// </summary>
    BestDiscount = 0,

    /// <summary>
    /// The campaign reduction only. The price's automatic and volume discounts are suppressed for
    /// as long as the campaign applies, and restored the moment it no longer does — a renewal past
    /// <see cref="CampaignKind.FirstAnnualPeriod"/>'s window, for example.
    /// </summary>
    ReplaceBuiltIn = 1,

    /// <summary>
    /// Both reductions, combined the same way two automatic discounts already are: added, not
    /// compounded, and capped at 100% of the gross.
    /// </summary>
    Stack = 2
}
