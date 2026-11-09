using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class CreditOfferRepository : ICreditOfferRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public CreditOfferRepository(IDbContextProvider db) => _db = db;

    public async Task<bool> TryCreateAsync(CreditOffer offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        await EnsureIndexesAsync(offer.TenantId, cancellationToken);

        try
        {
            await Collection(offer.TenantId).InsertOneAsync(offer, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<CreditOffer?> ResolveAsync(
        string tenantId, string? organizationId, string meterKey, CancellationToken cancellationToken) =>
        (string.IsNullOrWhiteSpace(organizationId)
            ? null
            : await FindAtScopeAsync(tenantId, organizationId, meterKey, cancellationToken))
        ?? await FindAtScopeAsync(tenantId, null, meterKey, cancellationToken);

    public Task<CreditOffer?> FindAtScopeAsync(
        string tenantId, string? organizationId, string meterKey, CancellationToken cancellationToken)
    {
        var filter = Builders<CreditOffer>.Filter;

        return Collection(tenantId)
            .Find(filter.And(
                filter.Eq(offer => offer.TenantId, tenantId),
                filter.Eq(offer => offer.OrganizationId, string.IsNullOrWhiteSpace(organizationId) ? null : organizationId),
                filter.Eq(offer => offer.MeterKey, meterKey)))
            .FirstOrDefaultAsync(cancellationToken)!;
    }

    public async Task<IReadOnlyList<CreditOffer>> ListAsync(
        string tenantId, string? organizationId, CancellationToken cancellationToken)
    {
        var filter = Builders<CreditOffer>.Filter;
        var scope = string.IsNullOrWhiteSpace(organizationId)
            ? filter.Eq(offer => offer.OrganizationId, null)
            : filter.In(offer => offer.OrganizationId, new string?[] { null, organizationId });

        return await Collection(tenantId)
            .Find(filter.And(filter.Eq(offer => offer.TenantId, tenantId), scope))
            .SortBy(offer => offer.MeterKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryUpdateAsync(CreditOffer offer, long expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        offer.Version = expectedVersion + 1;

        var filter = Builders<CreditOffer>.Filter;
        var result = await Collection(offer.TenantId).ReplaceOneAsync(
            filter.And(
                filter.Eq(item => item.TenantId, offer.TenantId),
                filter.Eq(item => item.ItemId, offer.ItemId),
                filter.Eq(item => item.Version, expectedVersion)),
            offer,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId)) return;
        await Collection(tenantId).Indexes.CreateManyAsync(
            SubscriptionCreditIndexDefinitions.CreateCreditOfferIndexes(), cancellationToken);
        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<CreditOffer> Collection(string tenantId) =>
        SubscriptionCollections.Of<CreditOffer>(_db, tenantId, SubscriptionCollections.CreditOffers);
}
