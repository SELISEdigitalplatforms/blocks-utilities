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

    /// <summary>
    /// The floor the derived balances clamp to, typed so the field never changes BSON type.
    /// </summary>
    private static readonly BsonDecimal128 ZeroQuantity = new(Decimal128.Zero);

    public SubscriptionUsageCurrentRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        var collection = Current(tenantId);

        // Dropped before the wider index is created, not after: a per-user row inserted in the
        // window between the two would still be rejected by the old index, which is unique on
        // subscription, meter and period alone and so treats a user's row as a duplicate of the
        // aggregate row already occupying that triple. A tenant that never had the old index — every
        // one created after this change — simply has nothing to drop.
        try
        {
            await collection.Indexes.DropOneAsync(
                SubscriptionIndexDefinitions.UsageCurrentLegacyUniqueIndexName, cancellationToken);
        }
        catch (MongoCommandException exception) when (exception.Code is 27 or 26)
        {
            // IndexNotFound (27) or NamespaceNotFound (26): nothing to drop, a fresh tenant or one
            // already migrated by an earlier call.
        }

        await collection.Indexes.CreateManyAsync(
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
            // Always the aggregate's own empty sentinel — this pipeline only ever writes the
            // aggregate row, never a per-user one (ApplyUserDeltaAsync writes those). Unconditional
            // so a document published before UserId existed picks it up on its very next publish,
            // rather than being permanently missing it: this $set pipeline never rewrites a document
            // wholesale, so an omitted field would otherwise never be added at all.
            { "UserId", incoming["UserId"] },
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
            // The meter's granularity is plan terms, so it belongs to the subscription's version
            // like the rest of them. In the balance group a late usage publish carrying
            // pre-plan-change terms could drive a widened scale back down, and a reader would then
            // format a figure to fewer places than it actually has.
            { "QuantityScale", When(subscriptionIsNewer, "QuantityScale") },
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
                    // Decimal128 rather than a long zero so the field keeps one BSON type whichever
                    // side of the floor wins. A consumer reading this collection directly should not
                    // have to handle Remaining arriving as an integer on the periods that clamped.
                    ZeroQuantity,
                    new BsonDocument("$subtract", new BsonArray { "$Included", "$Used" })
                })
            },
            {
                "Overage",
                new BsonDocument("$max", new BsonArray
                {
                    // Decimal128 rather than a long zero so the field keeps one BSON type whichever
                    // side of the floor wins. A consumer reading this collection directly should not
                    // have to handle Remaining arriving as an integer on the periods that clamped.
                    ZeroQuantity,
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
            .Find(CurrentWindowFilter(tenantId, organizationId, subscriptionId, asOfUtc) &
                  Builders<SubscriptionUsageCurrent>.Filter.Eq(current => current.UserId, string.Empty))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionUsageCurrent>> ListUserRowsAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        DateTime asOfUtc,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(CurrentWindowFilter(tenantId, organizationId, subscriptionId, asOfUtc) &
                  UserRowFilter())
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Matches a genuine per-user row, and only that.
    /// </summary>
    /// <remarks>
    /// Not just <c>Ne(UserId, "")</c>: Mongo's <c>$ne</c> also matches a document where the field is
    /// missing entirely, and every aggregate row written before <c>UserId</c> existed
    /// (<see cref="SubscriptionUsageCurrent.SchemaVersion"/> below 3) has no such field in its BSON
    /// at all. Without the explicit existence check, every one of those legacy aggregate rows was
    /// returned here as if it were a per-user row — doubling every meter in a subscription's current
    /// usage the moment it carried any pre-migration document, since the aggregate row was then read
    /// twice: once as itself, once mistaken for a user row with an empty id.
    /// </remarks>
    private static FilterDefinition<SubscriptionUsageCurrent> UserRowFilter() =>
        Builders<SubscriptionUsageCurrent>.Filter.And(
            Builders<SubscriptionUsageCurrent>.Filter.Exists(current => current.UserId),
            Builders<SubscriptionUsageCurrent>.Filter.Ne(current => current.UserId, string.Empty));

    private static FilterDefinition<SubscriptionUsageCurrent> CurrentWindowFilter(
        string tenantId,
        string organizationId,
        string subscriptionId,
        DateTime asOfUtc) =>
        Builders<SubscriptionUsageCurrent>.Filter.And(
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
                asOfUtc));

    public async Task<SubscriptionUsageCurrent> ApplyUserDeltaAsync(
        SubscriptionUsageCurrent seed,
        decimal delta,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);

        await EnsureIndexesAsync(seed.TenantId, cancellationToken);

        var now = DateTime.UtcNow;

        var update = Builders<SubscriptionUsageCurrent>.Update
            .Inc(current => current.Used, delta)
            .Inc(current => current.LedgerRecordCount, 1)
            .Set(current => current.Included, seed.Included)
            .Set(current => current.UpdatedAtUtc, now)
            .Set(current => current.ExpiresAtUtc, seed.ExpiresAtUtc)
            .SetOnInsert(current => current.TenantId, seed.TenantId)
            .SetOnInsert(current => current.OrganizationId, seed.OrganizationId)
            .SetOnInsert(current => current.SubscriptionId, seed.SubscriptionId)
            .SetOnInsert(current => current.UserId, seed.UserId)
            .SetOnInsert(current => current.SubscriptionStatus, seed.SubscriptionStatus)
            .SetOnInsert(current => current.PlanId, seed.PlanId)
            .SetOnInsert(current => current.PlanCode, seed.PlanCode)
            .SetOnInsert(current => current.MeterKey, seed.MeterKey)
            .SetOnInsert(current => current.UnitLabel, seed.UnitLabel)
            .SetOnInsert(current => current.QuantityScale, seed.QuantityScale)
            .SetOnInsert(current => current.PeriodKey, seed.PeriodKey)
            .SetOnInsert(current => current.PeriodStartUtc, seed.PeriodStartUtc)
            .SetOnInsert(current => current.PeriodEndUtc, seed.PeriodEndUtc)
            .SetOnInsert(current => current.OverageAllowed, seed.OverageAllowed)
            .SetOnInsert(current => current.SchemaVersion, seed.SchemaVersion);

        // ReturnDocument.After so the derived fields below are computed from the balance this
        // caller's own delta already landed in, the same guarantee ApplyDeltaAsync gives the
        // authoritative counter.
        var updated = await Current(seed.TenantId).FindOneAndUpdateAsync(
            Builders<SubscriptionUsageCurrent>.Filter.Eq(current => current.ItemId, seed.ItemId),
            update,
            new FindOneAndUpdateOptions<SubscriptionUsageCurrent>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        return await DeriveAsync(updated, cancellationToken);
    }

    /// <summary>
    /// Recomputes <c>Remaining</c> and <c>Overage</c> from whatever <c>Used</c> and <c>Included</c>
    /// just landed, and persists them.
    /// </summary>
    /// <remarks>
    /// A second small write rather than folding this into <see cref="ApplyUserDeltaAsync"/>'s own
    /// update: <c>$inc</c> and a pipeline-computed field cannot both apply in one
    /// <c>FindOneAndUpdateAsync</c> call built from <see cref="UpdateDefinition{TDocument}"/>
    /// combinators the way <see cref="ApplyUserDeltaAsync"/> is. The window between the two writes is
    /// harmless: <c>Used</c> and <c>Included</c> are already final, so a reader in between sees a
    /// correct balance with stale derived fields for, at most, one more round trip.
    /// </remarks>
    private async Task<SubscriptionUsageCurrent> DeriveAsync(
        SubscriptionUsageCurrent document,
        CancellationToken cancellationToken)
    {
        var remaining = Math.Max(0, document.Included - document.Used);
        var overage = Math.Max(0, document.Used - document.Included);

        await Current(document.TenantId).UpdateOneAsync(
            Builders<SubscriptionUsageCurrent>.Filter.Eq(current => current.ItemId, document.ItemId),
            Builders<SubscriptionUsageCurrent>.Update
                .Set(current => current.Remaining, remaining)
                .Set(current => current.Overage, overage),
            cancellationToken: cancellationToken);

        document.Remaining = remaining;
        document.Overage = overage;

        return document;
    }

    public async Task<bool> TryRepairUserRowAsync(
        string tenantId,
        SubscriptionUsageCurrent seed,
        decimal used,
        long ledgerRecordCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);

        await EnsureIndexesAsync(tenantId, cancellationToken);

        var remaining = Math.Max(0, seed.Included - used);
        var overage = Math.Max(0, used - seed.Included);
        var now = DateTime.UtcNow;

        var collection = Current(tenantId);

        var result = await collection.UpdateOneAsync(
            Builders<SubscriptionUsageCurrent>.Filter.And(
                Builders<SubscriptionUsageCurrent>.Filter.Eq(current => current.ItemId, seed.ItemId),
                Builders<SubscriptionUsageCurrent>.Filter.Lt(
                    current => current.LedgerRecordCount,
                    ledgerRecordCount)),
            Builders<SubscriptionUsageCurrent>.Update
                .Set(current => current.Used, used)
                .Set(current => current.LedgerRecordCount, ledgerRecordCount)
                .Set(current => current.Included, seed.Included)
                .Set(current => current.Remaining, remaining)
                .Set(current => current.Overage, overage)
                .Set(current => current.ExpiresAtUtc, seed.ExpiresAtUtc)
                .Set(current => current.UpdatedAtUtc, now),
            cancellationToken: cancellationToken);

        if (result.ModifiedCount == 1)
        {
            return true;
        }

        // Nothing to update means either the row is already current, or it does not exist yet — the
        // one case TryRepairCounterAsync never has to handle, because the counter always exists once
        // anything has been recorded against it. A user's row can be missing outright: a first
        // ApplyUserDeltaAsync call that never landed at all, discovered here from the ledger instead.
        seed.Used = used;
        seed.LedgerRecordCount = ledgerRecordCount;
        seed.Remaining = remaining;
        seed.Overage = overage;
        seed.UpdatedAtUtc = now;

        try
        {
            await collection.InsertOneAsync(seed, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Already repaired, or written since by a real recording. Retried once against the
            // guarded update rather than assumed stale, for the same reason TryPublishAsync retries
            // its own insert fallback once: whichever writer got there first may not have been this
            // one's own ledger figure.
            result = await collection.UpdateOneAsync(
                Builders<SubscriptionUsageCurrent>.Filter.And(
                    Builders<SubscriptionUsageCurrent>.Filter.Eq(current => current.ItemId, seed.ItemId),
                    Builders<SubscriptionUsageCurrent>.Filter.Lt(
                        current => current.LedgerRecordCount,
                        ledgerRecordCount)),
                Builders<SubscriptionUsageCurrent>.Update
                    .Set(current => current.Used, used)
                    .Set(current => current.LedgerRecordCount, ledgerRecordCount)
                    .Set(current => current.Included, seed.Included)
                    .Set(current => current.Remaining, remaining)
                    .Set(current => current.Overage, overage)
                    .Set(current => current.ExpiresAtUtc, seed.ExpiresAtUtc)
                    .Set(current => current.UpdatedAtUtc, now),
                cancellationToken: cancellationToken);

            return result.ModifiedCount == 1;
        }
    }

    public async Task<IReadOnlyList<SubscriptionUsageCurrent>> ListUserRowsBehindAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(Builders<SubscriptionUsageCurrent>.Filter.And(
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.TenantId,
                    tenantId),
                UserRowFilter(),
                Builders<SubscriptionUsageCurrent>.Filter.Lte(
                    current => current.PeriodStartUtc,
                    asOfUtc),
                Builders<SubscriptionUsageCurrent>.Filter.Gt(
                    current => current.PeriodEndUtc,
                    asOfUtc)))
            .SortBy(current => current.UpdatedAtUtc)
            .Limit(limit)
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

    public async Task<IReadOnlyList<SubscriptionUsageCurrent>> ListBySubscriptionAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(Builders<SubscriptionUsageCurrent>.Filter.And(
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.TenantId,
                    tenantId),
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.SubscriptionId,
                    subscriptionId)))
            .ToListAsync(cancellationToken);

    public async Task<bool> TryRetireAsync(
        string tenantId,
        string itemId,
        DateTime endUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Current(tenantId).UpdateOneAsync(
            Builders<SubscriptionUsageCurrent>.Filter.And(
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.ItemId,
                    itemId),
                Builders<SubscriptionUsageCurrent>.Filter.Gt(
                    current => current.PeriodEndUtc,
                    endUtc)),
            Builders<SubscriptionUsageCurrent>.Update
                .Set(current => current.PeriodEndUtc, endUtc)
                .Set(current => current.ExpiresAtUtc, expiresAtUtc),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    private IMongoCollection<SubscriptionUsageCurrent> Current(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageCurrent>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.UsageCurrent);
}
