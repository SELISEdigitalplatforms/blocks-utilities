using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionUsageCurrentRepository : ISubscriptionUsageCurrentRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionUsageCurrentRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Current(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsageCurrentIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<bool> TryPublishAsync(
        SubscriptionUsageCurrent document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await EnsureIndexesAsync(document.TenantId, cancellationToken);

        // Serialized through the entity's own mapping rather than assembled by hand, so the values in
        // the pipeline below are byte-for-byte what a typed write would have stored. That matters for
        // the dates in particular: a never-resetting window ends at DateTime.MaxValue, which the
        // typed serializer represents exactly and a hand-built BsonDateTime would round to
        // milliseconds.
        var incoming = document.ToBsonDocument();

        var filter = Builders<SubscriptionUsageCurrent>.Filter.And(
            Builders<SubscriptionUsageCurrent>.Filter.Eq(
                current => current.ItemId,
                document.ItemId),
            // Either version being newer is enough to be worth writing. Not the composite "counter
            // newer, or equal counter and newer subscription": that made the counter version
            // dominant, so a lifecycle refresh holding newer metadata was rejected outright whenever
            // a usage recording had already advanced the counter past it, and its metadata never
            // landed at all.
            Builders<SubscriptionUsageCurrent>.Filter.Or(
                Builders<SubscriptionUsageCurrent>.Filter.Lt(
                    current => current.CounterVersion,
                    document.CounterVersion),
                Builders<SubscriptionUsageCurrent>.Filter.Lt(
                    current => current.SubscriptionVersion,
                    document.SubscriptionVersion)));

        var update = Builders<SubscriptionUsageCurrent>.Update.Pipeline(
            BuildMergePipeline(incoming));

        var collection = Current(document.TenantId);

        // Update first, insert only if there is nothing to update - rather than one upsert. An upsert
        // whose filter matches nothing tries to insert, and the insert carries this document's own
        // composed _id, so every write that lost both version comparisons would raise a duplicate
        // key. Splitting them makes losing a version race a plain "modified nothing".
        if (await MergeAsync(collection, filter, update, cancellationToken))
        {
            return true;
        }

        try
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Somebody inserted it between the merge above and this insert. Tried once more, because
            // the merge may now have something newer to contribute; if it does not, it correctly
            // changes nothing. Once, not in a loop: the document exists from here on.
            return await MergeAsync(collection, filter, update, cancellationToken);
        }
    }

    /// <summary>
    /// Merges one published document into the stored one, each version governing its own fields.
    /// </summary>
    /// <remarks>
    /// A pipeline rather than a plain <c>$set</c>, because the two versions order two different
    /// groups of fields and a single conditional write of the whole document cannot honour both.
    /// <para>
    /// The failure it exists to prevent: a cancellation publishes
    /// <c>(counter 10, subscription 6, Cancelled)</c>, and a usage request already in flight then
    /// publishes <c>(counter 11, subscription 5, Active)</c>. Replacing the whole document because
    /// the counter is newer would restore <c>Active</c>, drive the stored subscription version
    /// backwards from 6 to 5, and leave a cancelled subscription advertising a live allowance.
    /// </para>
    /// <para>
    /// So the balance fields move only when the counter version is newer, the plan and status fields
    /// move only when the subscription version is newer, and each stored version becomes the maximum
    /// of the two. Every write is then idempotent and order-independent: the document converges on
    /// the newest of each kind of information whichever order the writers arrive in.
    /// </para>
    /// <para>
    /// <c>Remaining</c> and <c>Overage</c> are recomputed in a second stage from whichever
    /// <c>Used</c> and <c>Included</c> won, because they are pure functions of those two and taking
    /// either from the losing side would describe a balance that never existed. This is not the
    /// projection doing billing arithmetic - it is the same one-line function the authoritative
    /// response uses, evaluated where both of its inputs are final.
    /// </para>
    /// </remarks>
    private static PipelineDefinition<SubscriptionUsageCurrent, SubscriptionUsageCurrent>
        BuildMergePipeline(BsonDocument incoming)
    {
        // Missing on insert, so absent compares as "older than anything".
        var storedCounter = new BsonDocument("$ifNull", new BsonArray { "$CounterVersion", -1L });
        var storedSubscription =
            new BsonDocument("$ifNull", new BsonArray { "$SubscriptionVersion", -1L });

        var counterIsNewer = new BsonDocument(
            "$gt", new BsonArray { incoming["CounterVersion"], storedCounter });
        var subscriptionIsNewer = new BsonDocument(
            "$gt", new BsonArray { incoming["SubscriptionVersion"], storedSubscription });

        BsonDocument When(BsonDocument condition, string field) =>
            new("$cond", new BsonArray { condition, incoming[field], "$" + field });

        var merge = new BsonDocument
        {
            // Scope and identity. The same for the life of the document - the window is part of its
            // key - so they are written unconditionally and settle an insert.
            { "TenantId", incoming["TenantId"] },
            { "OrganizationId", incoming["OrganizationId"] },
            { "SubscriptionId", incoming["SubscriptionId"] },
            { "MeterKey", incoming["MeterKey"] },
            { "PeriodKey", incoming["PeriodKey"] },
            { "PeriodStartUtc", incoming["PeriodStartUtc"] },
            { "PeriodEndUtc", incoming["PeriodEndUtc"] },
            { "SchemaVersion", incoming["SchemaVersion"] },
            { "UpdatedAtUtc", incoming["UpdatedAtUtc"] },

            // Balance: the counter's to say.
            { "Used", When(counterIsNewer, "Used") },
            { "ExpiresAtUtc", When(counterIsNewer, "ExpiresAtUtc") },

            // Terms and status: the subscription's to say.
            { "SubscriptionStatus", When(subscriptionIsNewer, "SubscriptionStatus") },
            { "PlanId", When(subscriptionIsNewer, "PlanId") },
            { "PlanCode", When(subscriptionIsNewer, "PlanCode") },
            { "UnitLabel", When(subscriptionIsNewer, "UnitLabel") },
            { "OverageAllowed", When(subscriptionIsNewer, "OverageAllowed") },

            // The allowance belongs to neither version on its own, so it moves on either.
            //
            // It is computed by MeterAllowance.Effective from the plan's terms AND the counter's
            // LimitSnapshot — the allowance frozen when the window opened, which is where a
            // carry-forward from the previous period lands. So a change to the counter can change the
            // allowance with no plan change at all: a seed publishes the opening figure before any
            // counter exists, and the first recording opens the counter with a possibly different
            // frozen snapshot. Owned by the subscription version alone, that correction could only
            // arrive with an unrelated plan edit.
            //
            // Guarded so it cannot reopen the regression the field groups exist to prevent: a writer
            // whose subscription version is BEHIND what is stored may not touch the allowance, so a
            // late usage publish carrying pre-plan-change terms still cannot undo a newer plan's
            // figure. It may only correct the allowance when its own view of the subscription is at
            // least as current as the stored one.
            {
                "Included",
                new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$or", new BsonArray
                    {
                        subscriptionIsNewer,
                        new BsonDocument("$and", new BsonArray
                        {
                            counterIsNewer,
                            new BsonDocument("$gte", new BsonArray
                            {
                                incoming["SubscriptionVersion"],
                                storedSubscription
                            })
                        })
                    }),
                    incoming["Included"],
                    "$Included"
                })
            },

            // Each version keeps the higher of the two, so neither can be driven backwards by a
            // writer that only had newer information of the other kind.
            {
                "CounterVersion",
                new BsonDocument("$max", new BsonArray { storedCounter, incoming["CounterVersion"] })
            },
            {
                "SubscriptionVersion",
                new BsonDocument(
                    "$max", new BsonArray { storedSubscription, incoming["SubscriptionVersion"] })
            }
        };

        // A second stage, so it reads the merged Used and Included rather than the stored ones.
        var derive = new BsonDocument
        {
            {
                "Remaining",
                new BsonDocument("$max", new BsonArray
                {
                    0L,
                    new BsonDocument("$subtract", new BsonArray { "$Included", "$Used" })
                })
            },
            {
                "Overage",
                new BsonDocument("$max", new BsonArray
                {
                    0L,
                    new BsonDocument("$subtract", new BsonArray { "$Used", "$Included" })
                })
            }
        };

        return new BsonDocumentStagePipelineDefinition<
            SubscriptionUsageCurrent,
            SubscriptionUsageCurrent>(
            [
                new BsonDocument("$set", merge),
                new BsonDocument("$set", derive)
            ]);
    }

    private static async Task<bool> MergeAsync(
        IMongoCollection<SubscriptionUsageCurrent> collection,
        FilterDefinition<SubscriptionUsageCurrent> filter,
        UpdateDefinition<SubscriptionUsageCurrent> update,
        CancellationToken cancellationToken)
    {
        var result = await collection.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TrySeedAsync(
        SubscriptionUsageCurrent document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await EnsureIndexesAsync(document.TenantId, cancellationToken);

        try
        {
            await Current(document.TenantId).InsertOneAsync(
                document,
                cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Already published, by a real recording or by an earlier seed. Nothing to do, and
            // deliberately nothing written: a seed that overwrote a live balance with zero would
            // discard usage somebody has been billed for.
            return false;
        }
    }

    public async Task<IReadOnlyList<SubscriptionUsageCurrent>> ListCurrentAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        DateTime asOfUtc,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(Builders<SubscriptionUsageCurrent>.Filter.And(
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.TenantId,
                    tenantId),
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.OrganizationId,
                    organizationId),
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.SubscriptionId,
                    subscriptionId),
                Builders<SubscriptionUsageCurrent>.Filter.Lte(
                    current => current.PeriodStartUtc,
                    asOfUtc),
                // Strictly after: a period ends the instant its successor begins, so an inclusive
                // upper bound would return two windows for one meter at the boundary.
                Builders<SubscriptionUsageCurrent>.Filter.Gt(
                    current => current.PeriodEndUtc,
                    asOfUtc)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionUsageCurrent>> ListBehindCountersAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(Builders<SubscriptionUsageCurrent>.Filter.And(
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.TenantId,
                    tenantId),
                Builders<SubscriptionUsageCurrent>.Filter.Lte(
                    current => current.PeriodStartUtc,
                    asOfUtc),
                Builders<SubscriptionUsageCurrent>.Filter.Gt(
                    current => current.PeriodEndUtc,
                    asOfUtc)))
            // Served by ix_usage_current_tenant_updated, which is descending on UpdatedAtUtc; the
            // ascending sort here walks the same index backwards rather than sorting in memory.
            .SortBy(current => current.UpdatedAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<SubscriptionUsageCurrent?> GetAsync(
        string tenantId,
        string documentId,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(Builders<SubscriptionUsageCurrent>.Filter.Eq(
                current => current.ItemId,
                documentId))
            .FirstOrDefaultAsync(cancellationToken);

    private IMongoCollection<SubscriptionUsageCurrent> Current(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageCurrent>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.UsageCurrent);
}
