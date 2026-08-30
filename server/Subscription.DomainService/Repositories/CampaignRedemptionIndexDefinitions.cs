using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The index that makes "one use per organization" true, rather than merely checked.
/// </summary>
public static class CampaignRedemptionIndexDefinitions
{
    public const string OneUseIndexName = "campaign_redemption_one_use_per_org";
    public const string SubscriptionLookupIndexName = "campaign_redemption_by_subscription";
    public const string StaleReservationIndexName = "campaign_redemption_stale_reservations";

    public static IReadOnlyCollection<CreateIndexModel<CampaignRedemption>> CreateIndexes() =>
    [
        // Unique across the rows a one-use campaign actually needs to be unique for: one active
        // claim per organization and discount, where "active" is anything that is not Released.
        // A non-one-use campaign is entirely outside this index's filter and can accumulate as
        // many rows as it has subscriptions -- there is nothing to enforce there.
        //
        // Released is deliberately excluded from the filter, not merely from what a caller reads
        // back. Every row this feature writes stays in the collection forever -- an abandoned
        // subscription's own row, and whichever different subscription later reclaims the freed
        // slot, are two separate documents with two separate histories. Had a Released row still
        // counted toward this index, reclaiming a freed slot would have had nowhere to insert a
        // second document, and the only way to satisfy the index would have been to overwrite the
        // abandoned subscription's own row -- silently erasing which subscription actually held
        // the campaign before it was given back. A concurrency test caught exactly this before it
        // shipped.
        //
        // "$lt Released" rather than "$ne Released": MongoDB's partialFilterExpression supports
        // only equality and the ordered comparisons ($gt/$gte/$lt/$lte/$exists/$type), never $ne
        // or $in -- confirmed by running this against a real server, not assumed from the docs.
        // This works only because CampaignRedemptionState.Released is deliberately the highest
        // ordinal of a closed four-value enum, so "less than Released" and "not Released" are the
        // same set for every value that enum can hold. A state added after Released, rather than
        // before it, would silently break this index's meaning without changing a single line
        // here -- which is why CampaignRedemptionState's own doc comment calls this out at the
        // enum, not only at this one call site.
        new(
            Builders<CampaignRedemption>.IndexKeys
                .Ascending(redemption => redemption.TenantId)
                .Ascending(redemption => redemption.OrganizationId)
                .Ascending(redemption => redemption.DiscountId),
            new CreateIndexOptions<CampaignRedemption>
            {
                Unique = true,
                Name = OneUseIndexName,
                PartialFilterExpression = new BsonDocument
                {
                    { nameof(CampaignRedemption.OneUsePerOrganization), true },
                    {
                        nameof(CampaignRedemption.State),
                        new BsonDocument("$lt", (int)CampaignRedemptionState.Released)
                    }
                }
            }),
        new(
            Builders<CampaignRedemption>.IndexKeys
                .Ascending(redemption => redemption.TenantId)
                .Ascending(redemption => redemption.DiscountId)
                .Ascending(redemption => redemption.SubscriptionId),
            new CreateIndexOptions<CampaignRedemption> { Name = SubscriptionLookupIndexName }),
        // Backs the reconciliation sweep's search for a redemption stuck at Reserved or
        // ReleasePending -- the two states a crash between a subscription's own transition and
        // this ledger's paired call can leave behind. Deliberately not partial: State's
        // reconciled values are {Reserved, ReleasePending}, and a partial filter can only express
        // an ordered comparison, not that pair specifically -- Redeemed sits ordinally between
        // them. An ordinary compound index still serves an $in query over State fine; only a
        // partial filter's own definition is restricted this way.
        new(
            Builders<CampaignRedemption>.IndexKeys
                .Ascending(redemption => redemption.TenantId)
                .Ascending(redemption => redemption.State)
                .Ascending(redemption => redemption.LastUpdatedAtUtc)
                .Ascending(redemption => redemption.ItemId),
            new CreateIndexOptions<CampaignRedemption> { Name = StaleReservationIndexName })
    ];
}
