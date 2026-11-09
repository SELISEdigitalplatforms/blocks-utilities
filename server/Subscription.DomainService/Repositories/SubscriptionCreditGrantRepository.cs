using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>The Mongo-backed credit grant ledger.</summary>
public sealed class SubscriptionCreditGrantRepository : ISubscriptionCreditGrantRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public SubscriptionCreditGrantRepository(IDbContextProvider db) => _db = db;

    public async Task<bool> TryInsertAsync(SubscriptionCreditGrant grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        await EnsureIndexesAsync(grant.TenantId, cancellationToken);

        try
        {
            await Collection(grant.TenantId).InsertOneAsync(grant, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<SubscriptionCreditGrant?> GetAsync(
        string tenantId,
        string grantId,
        CancellationToken cancellationToken) =>
        await Collection(tenantId)
            .Find(grant => grant.TenantId == tenantId && grant.ItemId == grantId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionCreditGrant>> ListLiveAsync(
        string tenantId,
        string subscriptionId,
        string meterKey,
        int? seatNumber,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;

        var grants = await Collection(tenantId)
            .Find(filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.Eq(grant => grant.SubscriptionId, subscriptionId),
                filter.Eq(grant => grant.MeterKey, meterKey),
                filter.Eq(grant => grant.SeatNumber, seatNumber),
                filter.Eq(grant => grant.Status, CreditGrantStatus.Active),
                filter.Gt(grant => grant.Remaining, 0m),
                filter.Or(
                    filter.Eq(grant => grant.ExpiresAtUtc, null),
                    filter.Gt(grant => grant.ExpiresAtUtc, nowUtc))))
            .ToListAsync(cancellationToken);

        // ponytail: ordered in memory, because Mongo sorts a missing expiry first and the rule
        // also ranks by policy. Fine while a bucket holds tens of live grants; page from the index
        // if one ever holds thousands.
        return [.. SubscriptionCreditGrant.InConsumptionOrder(grants)];
    }

    public async Task<IReadOnlyList<SubscriptionCreditGrant>> ListAsync(
        string tenantId,
        string subscriptionId,
        string? meterKey,
        int? seatNumber,
        bool poolOnly,
        CreditGrantStatus? status,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;
        var clauses = new List<FilterDefinition<SubscriptionCreditGrant>>
        {
            filter.Eq(grant => grant.TenantId, tenantId),
            filter.Eq(grant => grant.SubscriptionId, subscriptionId)
        };

        if (meterKey is not null)
        {
            clauses.Add(filter.Eq(grant => grant.MeterKey, meterKey));
        }

        if (seatNumber is not null || poolOnly)
        {
            clauses.Add(filter.Eq(grant => grant.SeatNumber, seatNumber));
        }

        if (status is { } wanted)
        {
            clauses.Add(filter.Eq(grant => grant.Status, wanted));
        }

        return await Collection(tenantId)
            .Find(filter.And(clauses))
            .SortByDescending(grant => grant.GrantedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionCreditGrant>> ListLiveForOrganizationAsync(
        string tenantId,
        string organizationId,
        string meterKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;

        var grants = await Collection(tenantId)
            .Find(filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.Eq(grant => grant.OrganizationId, organizationId),
                filter.Eq(grant => grant.SubscriptionId, null),
                filter.Eq(grant => grant.MeterKey, meterKey),
                filter.Eq(grant => grant.Status, CreditGrantStatus.Active),
                filter.Gt(grant => grant.Remaining, 0m),
                filter.Or(
                    filter.Eq(grant => grant.ExpiresAtUtc, null),
                    filter.Gt(grant => grant.ExpiresAtUtc, nowUtc))))
            .ToListAsync(cancellationToken);

        return [.. SubscriptionCreditGrant.InConsumptionOrder(grants)];
    }

    public async Task<IReadOnlyList<SubscriptionCreditGrant>> ListForOrganizationAsync(
        string tenantId,
        string organizationId,
        string? meterKey,
        CreditGrantStatus? status,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;
        var clauses = new List<FilterDefinition<SubscriptionCreditGrant>>
        {
            filter.Eq(grant => grant.TenantId, tenantId),
            filter.Eq(grant => grant.OrganizationId, organizationId),
            filter.Eq(grant => grant.SubscriptionId, null)
        };

        if (meterKey is not null)
        {
            clauses.Add(filter.Eq(grant => grant.MeterKey, meterKey));
        }

        if (status is { } wanted)
        {
            clauses.Add(filter.Eq(grant => grant.Status, wanted));
        }

        return await Collection(tenantId)
            .Find(filter.And(clauses))
            .SortByDescending(grant => grant.GrantedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<SubscriptionCreditGrant?> TryConsumeAsync(
        string tenantId,
        string grantId,
        decimal quantity,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;
        var update = Builders<SubscriptionCreditGrant>.Update;

        // Everything that makes a grant spendable is in the filter, so the take and the checks are
        // one atomic write: a grant that expired, closed or ran short a moment ago simply does not
        // match, and the caller moves on to the next one.
        var taken = await Collection(tenantId).FindOneAndUpdateAsync(
            filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.Eq(grant => grant.ItemId, grantId),
                filter.Eq(grant => grant.Status, CreditGrantStatus.Active),
                filter.Gte(grant => grant.Remaining, quantity),
                filter.Or(
                    filter.Eq(grant => grant.ExpiresAtUtc, null),
                    filter.Gt(grant => grant.ExpiresAtUtc, nowUtc))),
            update
                .Inc(grant => grant.Remaining, -quantity)
                .Inc(grant => grant.Version, 1)
                .Set(grant => grant.LastUpdatedAtUtc, nowUtc),
            new FindOneAndUpdateOptions<SubscriptionCreditGrant>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        if (taken is not { Remaining: 0 })
        {
            return taken;
        }

        var closed = await Collection(tenantId).UpdateOneAsync(
            filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.Eq(grant => grant.ItemId, grantId),
                filter.Eq(grant => grant.Status, CreditGrantStatus.Active),
                filter.Eq(grant => grant.Remaining, 0m)),
            update
                .Set(grant => grant.Status, CreditGrantStatus.Exhausted)
                .Set(grant => grant.ClosedAtUtc, nowUtc)
                .Inc(grant => grant.Version, 1),
            cancellationToken: cancellationToken);

        if (closed.ModifiedCount > 0)
        {
            taken.Status = CreditGrantStatus.Exhausted;
            taken.ClosedAtUtc = nowUtc;
            taken.Version++;
        }

        return taken;
    }

    public async Task<IReadOnlyList<SubscriptionCreditGrant>> ListDueForCheckAsync(
        string tenantId,
        DateTime nowUtc,
        DateTime checkedBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;

        return await Collection(tenantId)
            .Find(filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.Eq(grant => grant.Status, CreditGrantStatus.Active),
                filter.Or(
                    filter.Lte(grant => grant.ExpiresAtUtc, nowUtc),
                    filter.Eq(grant => grant.LastCheckedAtUtc, null),
                    filter.Lt(grant => grant.LastCheckedAtUtc, checkedBeforeUtc))))
            .SortBy(grant => grant.LastCheckedAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryCloseAsync(
        string tenantId,
        string grantId,
        CreditGrantStatus status,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;

        var result = await Collection(tenantId).UpdateOneAsync(
            filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.Eq(grant => grant.ItemId, grantId),
                filter.Eq(grant => grant.Status, CreditGrantStatus.Active)),
            Builders<SubscriptionCreditGrant>.Update
                .Set(grant => grant.Status, status)
                .Set(grant => grant.ClosedAtUtc, nowUtc)
                .Set(grant => grant.LastCheckedAtUtc, nowUtc)
                .Set(grant => grant.LastUpdatedAtUtc, nowUtc)
                .Inc(grant => grant.Version, 1),
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    public async Task MarkCheckedAsync(
        string tenantId,
        IReadOnlyCollection<string> grantIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (grantIds.Count == 0)
        {
            return;
        }

        var filter = Builders<SubscriptionCreditGrant>.Filter;

        await Collection(tenantId).UpdateManyAsync(
            filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.In(grant => grant.ItemId, grantIds)),
            Builders<SubscriptionCreditGrant>.Update.Set(grant => grant.LastCheckedAtUtc, nowUtc),
            cancellationToken: cancellationToken);
    }

    public async Task<bool> RestoreAsync(
        string tenantId,
        string grantId,
        decimal quantity,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;
        var update = Builders<SubscriptionCreditGrant>.Update;

        var result = await Collection(tenantId).UpdateOneAsync(
            filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.Eq(grant => grant.ItemId, grantId),
                filter.In(
                    grant => grant.Status,
                    [CreditGrantStatus.Active, CreditGrantStatus.Exhausted])),
            update
                .Inc(grant => grant.Remaining, quantity)
                .Set(grant => grant.Status, CreditGrantStatus.Active)
                .Set(grant => grant.ClosedAtUtc, null)
                .Set(grant => grant.LastUpdatedAtUtc, nowUtc)
                .Inc(grant => grant.Version, 1),
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    // ---- the organization's outbox (spec 005) -------------------------------------------------

    public async Task<bool> TryAppendEventAsync(
        string tenantId,
        string grantId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outboxEvent);

        var filter = Builders<SubscriptionCreditGrant>.Filter;
        var result = await Collection(tenantId).UpdateOneAsync(
            filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.Eq(grant => grant.ItemId, grantId),
                filter.Not(filter.ElemMatch(
                    grant => grant.OutboxEvents,
                    existing => existing.DeduplicationKey == outboxEvent.DeduplicationKey))),
            Builders<SubscriptionCreditGrant>.Update.Push(grant => grant.OutboxEvents, outboxEvent),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<SubscriptionCreditGrant>> ListWithDueEventsAsync(
        string tenantId,
        DateTime dueAtUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;

        return await Collection(tenantId)
            .Find(filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.ElemMatch(
                    grant => grant.OutboxEvents,
                    outboxEvent =>
                        outboxEvent.Status != SubscriptionOutboxStatus.Published &&
                        outboxEvent.Status != SubscriptionOutboxStatus.Abandoned &&
                        (outboxEvent.NextAttemptAtUtc == null || outboxEvent.NextAttemptAtUtc <= dueAtUtc))))
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<SubscriptionOutboxEvent?> TryClaimEventAsync(
        string tenantId,
        string grantId,
        string eventId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;
        var updated = await Collection(tenantId).FindOneAndUpdateAsync(
            filter.And(
                filter.Eq(grant => grant.TenantId, tenantId),
                filter.Eq(grant => grant.ItemId, grantId),
                filter.ElemMatch(
                    grant => grant.OutboxEvents,
                    outboxEvent =>
                        outboxEvent.EventId == eventId &&
                        outboxEvent.Status != SubscriptionOutboxStatus.Published &&
                        outboxEvent.Status != SubscriptionOutboxStatus.Abandoned &&
                        (outboxEvent.LeaseExpiresAtUtc == null || outboxEvent.LeaseExpiresAtUtc <= DateTime.UtcNow))),
            Builders<SubscriptionCreditGrant>.Update
                .Set("OutboxEvents.$[evt].Status", SubscriptionOutboxStatus.Processing)
                .Set("OutboxEvents.$[evt].LeaseId", leaseId)
                .Set("OutboxEvents.$[evt].LeaseExpiresAtUtc", leaseExpiresAtUtc),
            new FindOneAndUpdateOptions<SubscriptionCreditGrant>
            {
                ArrayFilters = EventArrayFilter(eventId),
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        return updated?.OutboxEvents.Find(outboxEvent => outboxEvent.EventId == eventId);
    }

    public async Task MarkEventPublishedAsync(
        string tenantId,
        string grantId,
        string eventId,
        string leaseId,
        DateTime publishedAtUtc,
        bool clearPayload,
        CancellationToken cancellationToken)
    {
        var update = Builders<SubscriptionCreditGrant>.Update
            .Set("OutboxEvents.$[evt].Status", SubscriptionOutboxStatus.Published)
            .Set("OutboxEvents.$[evt].PublishedAtUtc", publishedAtUtc)
            .Set("OutboxEvents.$[evt].LeaseId", (string?)null)
            .Set("OutboxEvents.$[evt].LeaseExpiresAtUtc", (DateTime?)null);

        if (clearPayload)
        {
            update = update.Set("OutboxEvents.$[evt].Payload", string.Empty);
        }

        await Collection(tenantId).UpdateOneAsync(
            LeasedEventFilter(tenantId, grantId, eventId, leaseId),
            update,
            new UpdateOptions { ArrayFilters = EventArrayFilter(eventId) },
            cancellationToken);
    }

    public async Task MarkEventFailedAsync(
        string tenantId,
        string grantId,
        string eventId,
        string leaseId,
        SubscriptionOutboxStatus status,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string failureReason,
        CancellationToken cancellationToken) =>
        await Collection(tenantId).UpdateOneAsync(
            LeasedEventFilter(tenantId, grantId, eventId, leaseId),
            Builders<SubscriptionCreditGrant>.Update
                .Set("OutboxEvents.$[evt].Status", status)
                .Set("OutboxEvents.$[evt].AttemptCount", attemptCount)
                .Set("OutboxEvents.$[evt].NextAttemptAtUtc", nextAttemptAtUtc)
                .Set("OutboxEvents.$[evt].LastError", failureReason.Length <= 500 ? failureReason : failureReason[..500])
                .Set("OutboxEvents.$[evt].LeaseId", (string?)null)
                .Set("OutboxEvents.$[evt].LeaseExpiresAtUtc", (DateTime?)null),
            new UpdateOptions { ArrayFilters = EventArrayFilter(eventId) },
            cancellationToken);

    private static FilterDefinition<SubscriptionCreditGrant> LeasedEventFilter(
        string tenantId,
        string grantId,
        string eventId,
        string leaseId)
    {
        var filter = Builders<SubscriptionCreditGrant>.Filter;

        return filter.And(
            filter.Eq(grant => grant.TenantId, tenantId),
            filter.Eq(grant => grant.ItemId, grantId),
            filter.ElemMatch(
                grant => grant.OutboxEvents,
                outboxEvent => outboxEvent.EventId == eventId && outboxEvent.LeaseId == leaseId));
    }

    private static List<ArrayFilterDefinition> EventArrayFilter(string eventId) =>
    [
        new BsonDocumentArrayFilterDefinition<SubscriptionOutboxEvent>(new BsonDocument("evt.EventId", eventId))
    ];

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId)) return;
        await Collection(tenantId).Indexes.CreateManyAsync(
            SubscriptionCreditIndexDefinitions.CreateGrantIndexes(), cancellationToken);
        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<SubscriptionCreditGrant> Collection(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionCreditGrant>(
            _db, tenantId, SubscriptionCollections.CreditGrants);
}
