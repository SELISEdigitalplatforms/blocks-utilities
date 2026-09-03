using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionMerchantProfileRepository : ISubscriptionMerchantProfileRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public SubscriptionMerchantProfileRepository(IDbContextProvider db) => _db = db;

    public async Task<SubscriptionMerchantProfile?> GetAsync(
        string tenantId,
        CancellationToken cancellationToken) =>
        await Collection(tenantId)
            .Find(Scope(tenantId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionMerchantProfile> UpsertAsync(
        SubscriptionMerchantProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await EnsureIndexesAsync(profile.TenantId, cancellationToken);

        var now = DateTime.UtcNow;

        var update = Builders<SubscriptionMerchantProfile>.Update
            .SetOnInsert(item => item.ItemId, profile.ItemId)
            .SetOnInsert(item => item.TenantId, profile.TenantId)
            .SetOnInsert(item => item.CreatedAtUtc, now)
            .Set(item => item.LegalName, profile.LegalName)
            .Set(item => item.DisplayName, profile.DisplayName)
            .Set(item => item.Address, profile.Address)
            .Set(item => item.TaxRegistrationId, profile.TaxRegistrationId)
            .Set(item => item.SupportEmail, profile.SupportEmail)
            .Set(item => item.PaymentInstructions, profile.PaymentInstructions)
            .Set(item => item.LogoFileId, profile.LogoFileId)
            .Set(item => item.PrimaryColor, profile.PrimaryColor)
            .Set(item => item.AccentColor, profile.AccentColor)
            .Set(item => item.PaymentProviderName, profile.PaymentProviderName)
            .Set(item => item.LastUpdatedByUserId, profile.LastUpdatedByUserId)
            .Set(item => item.LastUpdatedDateUtc, now)
            .Inc(item => item.Version, 1);

        return await Collection(profile.TenantId).FindOneAndUpdateAsync(
            Scope(profile.TenantId),
            update,
            new FindOneAndUpdateOptions<SubscriptionMerchantProfile>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    private static FilterDefinition<SubscriptionMerchantProfile> Scope(string tenantId) =>
        Builders<SubscriptionMerchantProfile>.Filter.Eq(item => item.TenantId, tenantId);

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId))
        {
            return;
        }

        await Collection(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateMerchantProfileIndexes(),
            cancellationToken);

        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<SubscriptionMerchantProfile> Collection(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionMerchantProfile>(
            _db,
            tenantId,
            SubscriptionCollections.MerchantProfiles);
}
