using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One subscription's durable claim on a campaign discount.
/// </summary>
/// <remarks>
/// This is the mechanism that makes "one use per organization" actually true rather than merely
/// likely. A campaign's rule lives on <see cref="Discount.Campaign"/>; this is the ledger that
/// enforces it, because a rule checked only in application code before an insert is a race, and a
/// unique index checked by the database is not.
/// <para>
/// One row per (tenant, organization, discount) that a one-use campaign has ever been reserved
/// for — never deleted, so a released slot's history is not lost the moment it frees up. A
/// non-one-use campaign can accumulate many rows for the same discount across different
/// subscriptions; a one-use one is guaranteed exactly one row that is not
/// <see cref="CampaignRedemptionState.Released"/> at any moment, by
/// <see cref="Repositories.CampaignRedemptionIndexDefinitions"/>'s partial unique index rather than
/// by anything checked here.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class CampaignRedemption
{
    [BsonId] public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Never null, unlike <see cref="Discount.OrganizationId"/>. A tenant-wide campaign is still
    /// redeemed by one specific organization, and that organization is exactly the scope one-use
    /// enforcement has to key on -- a null here would make every organization in the tenant share
    /// one slot on a tenant-wide one-use campaign, which is not what "one use per organization"
    /// means.
    /// </summary>
    public string OrganizationId { get; set; } = string.Empty;

    public string DiscountId { get; set; } = string.Empty;

    /// <summary>
    /// The <see cref="Discount.Version"/> this reservation was accepted at. A later edit to the
    /// catalogue entry must never be able to reprice an already-reserved redemption -- this is
    /// what a reconciliation sweep or an audit reads back to confirm which terms actually applied,
    /// independent of what the catalogue entry says today.
    /// </summary>
    public long CampaignVersion { get; set; }

    public string SubscriptionId { get; set; } = string.Empty;

    public CampaignRedemptionState State { get; set; } = CampaignRedemptionState.Reserved;

    /// <summary>
    /// Copied from <see cref="Entities.CampaignTerms.OneUsePerOrganization"/> at reservation time,
    /// not read live from the catalogue. This is the field
    /// <see cref="Repositories.CampaignRedemptionIndexDefinitions"/>'s partial unique index
    /// actually filters on -- a campaign that is not one-use may accumulate more than one row for
    /// the same organization and discount, and the index has to know which kind of row this is
    /// without a second lookup.
    /// </summary>
    public bool OneUsePerOrganization { get; set; }

    /// <summary>
    /// The credit purchase this redemption was made for, or null for a subscription's.
    /// </summary>
    /// <remarks>
    /// A credit purchase redeems a code too, and its row carries the purchase record's id in
    /// <see cref="SubscriptionId"/> rather than the subscription's: a subscription can buy credits
    /// many times, and keyed by the subscription a second purchase would read as a retry of the
    /// first and be let through a one-use code. Set so the reconciliation sweep settles the row
    /// from the purchase's status instead of looking for a subscription by that id.
    /// </remarks>
    public string? CreditPurchaseId { get; set; }

    public DateTime ReservedAtUtc { get; set; }
    public DateTime? RedeemedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; }
}
