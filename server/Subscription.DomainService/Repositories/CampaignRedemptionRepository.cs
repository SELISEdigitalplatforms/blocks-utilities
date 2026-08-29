using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The Mongo-backed ledger behind <see cref="ICampaignRedemptionRepository"/>.
/// </summary>
/// <remarks>
/// The reservation this exists for is not "insert, and handle the duplicate-key exception if
/// somebody beat us to it" — that pattern (used by
/// <see cref="Payment.DomainService.Entities.BillingAccount"/>'s reconciliation, for instance)
/// answers "did my write happen", and every writer there was writing the <em>same</em> intended
/// values, so losing a race to an equivalent write is harmless. Here two different subscriptions
/// racing for one slot must produce two different outcomes for two different callers, so the
/// filter, the update, and the exception handler below all exist to answer the sharper question
/// "whose subscription actually holds this now."
/// </remarks>
public sealed class CampaignRedemptionRepository : ICampaignRedemptionRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public CampaignRedemptionRepository(IDbContextProvider db) => _db = db;

    public async Task<CampaignReservationOutcome> TryReserveAsync(
        CampaignRedemption reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        await EnsureIndexesAsync(reservation.TenantId, cancellationToken);

        // A retry of the same subscription's own attempt, whether one-use or not: its row already
        // exists regardless of state, and that is success either way -- including when the state
        // is already Redeemed, which a genuine retry of subscription creation cannot actually
        // reach in practice (creation runs before activation), but which this must not treat as a
        // conflict if it somehow did.
        var existing = await FindAsync(
            reservation.TenantId, reservation.DiscountId, reservation.SubscriptionId,
            cancellationToken);

        if (existing is not null)
        {
            return CampaignReservationOutcome.AlreadyReservedBySameSubscription;
        }

        reservation.State = CampaignRedemptionState.Reserved;
        reservation.LastUpdatedAtUtc = reservation.ReservedAtUtc;

        try
        {
            await Collection(reservation.TenantId)
                .InsertOneAsync(reservation, cancellationToken: cancellationToken);

            return CampaignReservationOutcome.Reserved;
        }
        catch (Exception exception) when (reservation.OneUsePerOrganization && IsDuplicateKey(exception))
        {
            // CampaignRedemptionIndexDefinitions.OneUseIndexName refused this insert: an active
            // (non-Released) claim already exists for this organization and discount. Whose it is
            // still has to be re-read rather than assumed -- the FindAsync check above and this
            // insert are two separate operations, and two concurrent attempts for the very same
            // subscription can both pass that check before either one inserts. The loser lands
            // here even though the winner was its own retry, not a stranger's, so this is
            // resolved the only way it safely can be: read what is actually there now.
            var current = await FindAsync(
                reservation.TenantId, reservation.DiscountId, reservation.SubscriptionId,
                cancellationToken);

            return current is not null
                ? CampaignReservationOutcome.AlreadyReservedBySameSubscription
                : CampaignReservationOutcome.HeldByAnotherSubscription;
        }
    }

    public async Task TryMarkRedeemedAsync(
        string tenantId,
        string discountId,
        string subscriptionId,
        DateTime redeemedAtUtc,
        CancellationToken cancellationToken)
    {
        await Collection(tenantId).UpdateOneAsync(
            Builders<CampaignRedemption>.Filter.And(
                Builders<CampaignRedemption>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<CampaignRedemption>.Filter.Eq(item => item.DiscountId, discountId),
                Builders<CampaignRedemption>.Filter.Eq(item => item.SubscriptionId, subscriptionId),
                // Only from Reserved. Idempotent against a repeat call -- already Redeemed matches
                // nothing here and ModifiedCount is silently zero, which is the correct outcome for
                // a duplicate activation event rather than an error.
                Builders<CampaignRedemption>.Filter.Eq(
                    item => item.State, CampaignRedemptionState.Reserved)),
            Builders<CampaignRedemption>.Update
                .Set(item => item.State, CampaignRedemptionState.Redeemed)
                .Set(item => item.RedeemedAtUtc, redeemedAtUtc)
                .Set(item => item.LastUpdatedAtUtc, redeemedAtUtc),
            cancellationToken: cancellationToken);
    }

    public async Task TryReleaseAsync(
        string tenantId,
        string discountId,
        string subscriptionId,
        DateTime releasedAtUtc,
        CancellationToken cancellationToken)
    {
        var collection = Collection(tenantId);
        var byKey = Builders<CampaignRedemption>.Filter.And(
            Builders<CampaignRedemption>.Filter.Eq(item => item.TenantId, tenantId),
            Builders<CampaignRedemption>.Filter.Eq(item => item.DiscountId, discountId),
            Builders<CampaignRedemption>.Filter.Eq(item => item.SubscriptionId, subscriptionId));

        // Reserved -> ReleasePending first and committed on its own, so a crash before the second
        // step below leaves a state a reconciliation sweep can find and finish -- rather than a
        // Reserved row indistinguishable from one nothing has happened to. Never touches an
        // already-Redeemed row: once a campaign activated, a later cancellation does not give it
        // back.
        await collection.UpdateOneAsync(
            Builders<CampaignRedemption>.Filter.And(
                byKey,
                Builders<CampaignRedemption>.Filter.Eq(
                    item => item.State, CampaignRedemptionState.Reserved)),
            Builders<CampaignRedemption>.Update
                .Set(item => item.State, CampaignRedemptionState.ReleasePending)
                .Set(item => item.LastUpdatedAtUtc, releasedAtUtc),
            cancellationToken: cancellationToken);

        // Idempotent against a repeat call: already Released matches nothing here and
        // ModifiedCount is silently zero.
        await collection.UpdateOneAsync(
            Builders<CampaignRedemption>.Filter.And(
                byKey,
                Builders<CampaignRedemption>.Filter.Eq(
                    item => item.State, CampaignRedemptionState.ReleasePending)),
            Builders<CampaignRedemption>.Update
                .Set(item => item.State, CampaignRedemptionState.Released)
                .Set(item => item.ReleasedAtUtc, releasedAtUtc)
                .Set(item => item.LastUpdatedAtUtc, releasedAtUtc),
            cancellationToken: cancellationToken);
    }

    public Task<CampaignRedemption?> FindAsync(
        string tenantId, string discountId, string subscriptionId, CancellationToken cancellationToken) =>
        Collection(tenantId).Find(Builders<CampaignRedemption>.Filter.And(
                Builders<CampaignRedemption>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<CampaignRedemption>.Filter.Eq(item => item.DiscountId, discountId),
                Builders<CampaignRedemption>.Filter.Eq(item => item.SubscriptionId, subscriptionId)))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<CampaignRedemption?> FindActiveForOrganizationAsync(
        string tenantId,
        string organizationId,
        string discountId,
        CancellationToken cancellationToken) =>
        Collection(tenantId).Find(Builders<CampaignRedemption>.Filter.And(
                Builders<CampaignRedemption>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<CampaignRedemption>.Filter.Eq(item => item.OrganizationId, organizationId),
                Builders<CampaignRedemption>.Filter.Eq(item => item.DiscountId, discountId),
                Builders<CampaignRedemption>.Filter.Ne(item => item.State, CampaignRedemptionState.Released)))
            .FirstOrDefaultAsync(cancellationToken);

    private static readonly CampaignRedemptionState[] ReconcilableStates =
    [
        CampaignRedemptionState.Reserved,
        CampaignRedemptionState.ReleasePending
    ];

    public async Task<IReadOnlyList<CampaignRedemption>> ListStaleAsync(
        string tenantId, DateTime reservedBeforeUtc, int limit, CancellationToken cancellationToken) =>
        await Collection(tenantId)
            .Find(Builders<CampaignRedemption>.Filter.And(
                Builders<CampaignRedemption>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<CampaignRedemption>.Filter.In(item => item.State, ReconcilableStates),
                Builders<CampaignRedemption>.Filter.Lte(item => item.LastUpdatedAtUtc, reservedBeforeUtc)))
            .SortBy(item => item.LastUpdatedAtUtc)
            .ThenBy(item => item.ItemId)
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task DeferAsync(
        string tenantId,
        string redemptionId,
        DateTime retryAfterUtc,
        CancellationToken cancellationToken) =>
        await Collection(tenantId).UpdateOneAsync(
            Builders<CampaignRedemption>.Filter.And(
                Builders<CampaignRedemption>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<CampaignRedemption>.Filter.Eq(item => item.ItemId, redemptionId),
                Builders<CampaignRedemption>.Filter.In(item => item.State, ReconcilableStates)),
            Builders<CampaignRedemption>.Update.Set(item => item.LastUpdatedAtUtc, retryAfterUtc),
            cancellationToken: cancellationToken);

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId)) return;
        await Collection(tenantId).Indexes.CreateManyAsync(
            CampaignRedemptionIndexDefinitions.CreateIndexes(), cancellationToken);
        _indexed.TryAdd(tenantId, 0);
    }

    private const int DuplicateKeyErrorCode = 11000;

    private static bool IsDuplicateKey(Exception exception) =>
        exception switch
        {
            MongoWriteException write =>
                write.WriteError?.Category == ServerErrorCategory.DuplicateKey,
            MongoCommandException command => command.Code == DuplicateKeyErrorCode,
            _ => false
        };

    private IMongoCollection<CampaignRedemption> Collection(string tenantId) =>
        SubscriptionCollections.Of<CampaignRedemption>(
            _db, tenantId, SubscriptionCollections.CampaignRedemptions);
}
