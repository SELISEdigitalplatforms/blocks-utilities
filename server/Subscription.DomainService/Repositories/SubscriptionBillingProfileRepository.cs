using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionBillingProfileRepository : ISubscriptionBillingProfileRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public SubscriptionBillingProfileRepository(IDbContextProvider db) => _db = db;

    public async Task<SubscriptionBillingProfile?> GetAsync(
        string tenantId,
        string organizationId,
        CancellationToken cancellationToken) =>
        await Collection(tenantId)
            .Find(Scope(tenantId, organizationId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionBillingProfile> UpsertAsync(
        SubscriptionBillingProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await EnsureIndexesAsync(profile.TenantId, cancellationToken);

        var now = DateTime.UtcNow;

        // Field-by-field rather than a whole-document replace, so the contacts the money path has
        // recorded are not wiped by an authoring request that knows nothing about them.
        var update = Builders<SubscriptionBillingProfile>.Update
            .SetOnInsert(item => item.ItemId, profile.ItemId)
            .SetOnInsert(item => item.TenantId, profile.TenantId)
            .SetOnInsert(item => item.OrganizationId, profile.OrganizationId)
            .SetOnInsert(item => item.CreatedAtUtc, now)
            .SetOnInsert(item => item.Contacts, [])
            .Set(item => item.LegalName, profile.LegalName)
            .Set(item => item.DisplayName, profile.DisplayName)
            .Set(item => item.BillingContactName, profile.BillingContactName)
            .Set(item => item.BillingContactEmail, profile.BillingContactEmail)
            .Set(item => item.Address, profile.Address)
            .Set(item => item.TaxRegistrationId, profile.TaxRegistrationId)
            .Set(item => item.LastUpdatedDateUtc, now)
            .Inc(item => item.Version, 1);

        return await Collection(profile.TenantId).FindOneAndUpdateAsync(
            Scope(profile.TenantId, profile.OrganizationId),
            update,
            new FindOneAndUpdateOptions<SubscriptionBillingProfile>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    public async Task RecordContactAsync(
        string tenantId,
        string organizationId,
        BillingContact contact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);

        if (string.IsNullOrWhiteSpace(contact.UserId))
        {
            return;
        }

        await EnsureIndexesAsync(tenantId, cancellationToken);

        // Remove then add, because the person's name or address may have changed since they last
        // acted and the document has to carry the current one. Two statements rather than a
        // positional update: the array is small, and a positional update cannot insert.
        await Collection(tenantId).UpdateOneAsync(
            Scope(tenantId, organizationId),
            Builders<SubscriptionBillingProfile>.Update.PullFilter(
                item => item.Contacts,
                existing => existing.UserId == contact.UserId),
            cancellationToken: cancellationToken);

        await Collection(tenantId).UpdateOneAsync(
            Scope(tenantId, organizationId),
            Builders<SubscriptionBillingProfile>.Update
                .Push(item => item.Contacts, contact)
                .Set(item => item.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    private static FilterDefinition<SubscriptionBillingProfile> Scope(
        string tenantId,
        string organizationId) =>
        Builders<SubscriptionBillingProfile>.Filter.And(
            Builders<SubscriptionBillingProfile>.Filter.Eq(item => item.TenantId, tenantId),
            Builders<SubscriptionBillingProfile>.Filter.Eq(
                item => item.OrganizationId,
                organizationId));

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId))
        {
            return;
        }

        await Collection(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateBillingProfileIndexes(),
            cancellationToken);

        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<SubscriptionBillingProfile> Collection(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionBillingProfile>(
            _db,
            tenantId,
            SubscriptionCollections.BillingProfiles);
}
