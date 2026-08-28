using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A reduction applied to this subscription's charges.
/// </summary>
/// <remarks>
/// Snapshotted from the discount catalogue and applied to signup, renewal and proration.
/// Percentages are basis points so a
/// third off is exact rather than 33.33 rounded somewhere unpredictable.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class DiscountTerms
{
    public string Code { get; set; } = string.Empty;

    public DiscountKind Kind { get; set; } = DiscountKind.Percent;

    /// <summary>Basis points off, when the kind is a percentage. 2500 is a quarter off.</summary>
    public int? PercentBasisPoints { get; set; }

    /// <summary>Minor units off, when the kind is a fixed amount.</summary>
    public long? AmountMinor { get; set; }

    /// <summary>How many periods it applies to. Null runs for the life of the subscription.</summary>
    public int? DurationPeriods { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>
    /// The plans this code was authored for, copied from the catalogue entry. Empty is unrestricted.
    /// </summary>
    /// <remarks>
    /// Snapshotted rather than looked up, for the reason every other term here is: the catalogue
    /// entry can be retired or re-scoped, and the subscriber has to be judged by the offer they
    /// accepted. Carried so a <em>later</em> move to another plan or price can be checked against the
    /// same restriction the redemption was — without this, a monthly-only code keeps discounting an
    /// annual price after a plan change, because nothing downstream can tell it was restricted.
    /// </remarks>
    public List<string> ApplicablePlanCodes { get; set; } = [];

    /// <summary>The prices this code was authored for. Empty is unrestricted.</summary>
    public List<string> ApplicablePriceIds { get; set; } = [];

    /// <summary>
    /// The catalogue entry this was copied from, and the <see cref="Discount.Version"/> it was
    /// copied at. Carried so a later reconciliation or audit can point back at the campaign that
    /// produced this snapshot -- never re-read from the catalogue, for the same reason nothing
    /// else on this snapshot is: the catalogue entry can move on, and this subscription must not.
    /// </summary>
    public string? DiscountId { get; set; }

    public long DiscountVersion { get; set; }

    /// <summary>
    /// When this snapshot was accepted -- the instant a checkout or activation redeemed it, not
    /// when the discount catalogue entry was created. Absent on a subscription created before this
    /// field existed.
    /// </summary>
    public DateTime? RedeemedAtUtc { get; set; }

    /// <summary>
    /// The campaign rules accepted at redemption, copied whole from <see cref="Discount.Campaign"/>
    /// at the instant this snapshot was taken. Its own <see cref="CampaignTerms.Kind"/> is the gate
    /// every campaign-specific code path checks -- a snapshot taken from a
    /// <see cref="CampaignKind.Standard"/> discount, or one taken before campaigns existed, carries
    /// a default-constructed <see cref="CampaignTerms"/> whose <c>Kind</c> is
    /// <see cref="CampaignKind.Standard"/>, and every campaign path treats that identically to
    /// there being no campaign at all.
    /// <para>
    /// A later edit or archival of the catalogue entry never reaches this: the subscriber is judged
    /// by the terms they redeemed, and that is exactly what a snapshot exists to freeze.
    /// </para>
    /// </summary>
    public CampaignTerms Campaign { get; set; } = new();
}
