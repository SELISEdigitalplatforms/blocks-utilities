using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public sealed class StoredPaymentMethodRepository : IStoredPaymentMethodRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();
    public StoredPaymentMethodRepository(IDbContextProvider dbContextProvider) => _dbContextProvider = dbContextProvider;

    public Task<List<StoredPaymentMethod>> ListActiveAsync(string tenantId, string shopperReference, CancellationToken cancellationToken) =>
        Collection(tenantId).Find(x => x.TenantId == tenantId && x.ShopperReference == shopperReference && x.Status == PaymentMethodStatus.Active)
            .SortByDescending(x => x.UpdatedAtUtc).Limit(200).ToListAsync(cancellationToken);

    public Task<StoredPaymentMethod?> GetAsync(string tenantId, string itemId, CancellationToken cancellationToken) =>
        Collection(tenantId).Find(x => x.TenantId == tenantId && x.ItemId == itemId).FirstOrDefaultAsync(cancellationToken)!;

    public async Task UpsertFromProviderAsync(StoredPaymentMethod method, DateTime eventDateUtc, CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(method.TenantId, cancellationToken);
        var filter = Builders<StoredPaymentMethod>.Filter.And(
            Builders<StoredPaymentMethod>.Filter.Eq(x => x.TenantId, method.TenantId),
            Builders<StoredPaymentMethod>.Filter.Eq(x => x.ShopperReference, method.ShopperReference),
            Builders<StoredPaymentMethod>.Filter.Eq(x => x.StoredPaymentMethodToken, method.StoredPaymentMethodToken),
            Builders<StoredPaymentMethod>.Filter.Or(
                Builders<StoredPaymentMethod>.Filter.Exists(x => x.LastProviderEventAtUtc, false),
                Builders<StoredPaymentMethod>.Filter.Lte(x => x.LastProviderEventAtUtc, eventDateUtc)));
        var update = Builders<StoredPaymentMethod>.Update
            .SetOnInsert(x => x.ItemId, method.ItemId)
            .SetOnInsert(x => x.CreatedAtUtc, method.CreatedAtUtc)
            .Set(x => x.TenantId, method.TenantId)
            .Set(x => x.ShopperReference, method.ShopperReference)
            .Set(x => x.ProviderName, method.ProviderName)
            .Set(x => x.StoredPaymentMethodToken, method.StoredPaymentMethodToken)
            .Set(x => x.Type, method.Type)
            .Set(x => x.Brand, method.Brand)
            .Set(x => x.LastFour, method.LastFour)
            .Set(x => x.ExpiryMonth, method.ExpiryMonth)
            .Set(x => x.ExpiryYear, method.ExpiryYear)
            .Set(x => x.FundingSource, method.FundingSource)
            .Set(x => x.IssuerCountry, method.IssuerCountry)
            .Set(x => x.Status, method.Status)
            .Set(x => x.LastProviderEventAtUtc, eventDateUtc)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow);
        try
        {
            await Collection(method.TenantId).UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // A newer provider event already owns the unique token record. Older events are safe no-ops.
        }
    }

    public Task MarkDeletionUnknownAsync(string tenantId, string itemId, DateTime nextAttemptAtUtc, CancellationToken cancellationToken) =>
        Collection(tenantId).UpdateOneAsync(x => x.TenantId == tenantId && x.ItemId == itemId && x.Status != PaymentMethodStatus.Disabled,
            Builders<StoredPaymentMethod>.Update
                .Set(x => x.Status, PaymentMethodStatus.DeletionUnknown)
                .Set(x => x.NextDeletionAttemptAtUtc, nextAttemptAtUtc)
                .Inc(x => x.DeletionAttemptCount, 1)
                .Set(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken: cancellationToken);

    public Task MarkDisabledAsync(string tenantId, string itemId, DateTime eventDateUtc, CancellationToken cancellationToken) =>
        Collection(tenantId).UpdateOneAsync(
            x => x.TenantId == tenantId && x.ItemId == itemId && x.LastProviderEventAtUtc <= eventDateUtc,
            Builders<StoredPaymentMethod>.Update
                .Set(x => x.Status, PaymentMethodStatus.Disabled)
                .Set(x => x.LastProviderEventAtUtc, eventDateUtc)
                .Set(x => x.NextDeletionAttemptAtUtc, null)
                .Set(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken: cancellationToken);

    public Task<List<StoredPaymentMethod>> GetUnknownDeletionsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken) =>
        Collection(tenantId).Find(x => x.TenantId == tenantId && x.Status == PaymentMethodStatus.DeletionUnknown && x.NextDeletionAttemptAtUtc <= utcNow)
            .SortBy(x => x.NextDeletionAttemptAtUtc).Limit(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken);

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId)) return;
        await Collection(tenantId).Indexes.CreateManyAsync([
            new CreateIndexModel<StoredPaymentMethod>(
                Builders<StoredPaymentMethod>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.ShopperReference).Ascending(x => x.StoredPaymentMethodToken),
                new CreateIndexOptions { Unique = true, Name = "ux_method_tenant_shopper_token" }),
            new CreateIndexModel<StoredPaymentMethod>(
                Builders<StoredPaymentMethod>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.ItemId),
                new CreateIndexOptions { Unique = true, Name = "ux_method_tenant_item" }),
            new CreateIndexModel<StoredPaymentMethod>(
                Builders<StoredPaymentMethod>.IndexKeys.Ascending(x => x.ShopperReference).Ascending(x => x.Status),
                new CreateIndexOptions { Name = "ix_method_shopper_status" }),
            new CreateIndexModel<StoredPaymentMethod>(
                Builders<StoredPaymentMethod>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.NextDeletionAttemptAtUtc),
                new CreateIndexOptions { Name = "ix_method_deletion_due" })
        ], cancellationToken);
        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<StoredPaymentMethod> Collection(string tenantId) =>
        _dbContextProvider.GetDatabase(tenantId).GetCollection<StoredPaymentMethod>("StoredPaymentMethods");
}
