using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class BillingAccountRepository : IBillingAccountRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public BillingAccountRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Accounts(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateBillingAccountIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<BillingAccount> GetOrCreateAsync(
        BillingAccount account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        await EnsureIndexesAsync(account.TenantId, cancellationToken);

        var existing = await FindAsync(
            account.TenantId,
            account.OrganizationId,
            account.ProviderName,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        try
        {
            await Accounts(account.TenantId)
                .InsertOneAsync(account, cancellationToken: cancellationToken);

            return account;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another request created it between the read and the insert. Its document is as
            // good as the one this call would have written.
            return await FindAsync(
                       account.TenantId,
                       account.OrganizationId,
                       account.ProviderName,
                       cancellationToken)
                   ?? account;
        }
    }

    public async Task<BillingAccount?> GetAsync(
        string tenantId,
        string billingAccountId,
        CancellationToken cancellationToken) =>
        await Accounts(tenantId)
            .Find(Builders<BillingAccount>.Filter.And(
                Builders<BillingAccount>.Filter.Eq(
                    account => account.TenantId,
                    tenantId),
                Builders<BillingAccount>.Filter.Eq(
                    account => account.ItemId,
                    billingAccountId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TrySetProviderCustomerAsync(
        string tenantId,
        string billingAccountId,
        string providerCustomerId,
        string? defaultPaymentMethodId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<BillingAccount>.Filter.And(
            Builders<BillingAccount>.Filter.Eq(
                account => account.TenantId,
                tenantId),
            Builders<BillingAccount>.Filter.Eq(
                account => account.ItemId,
                billingAccountId),
            Builders<BillingAccount>.Filter.Or(
                Builders<BillingAccount>.Filter.Eq(
                    account => account.ProviderCustomerId,
                    null),
                Builders<BillingAccount>.Filter.Eq(
                    account => account.ProviderCustomerId,
                    providerCustomerId)));

        var update = Builders<BillingAccount>.Update
            .Set(account => account.ProviderCustomerId, providerCustomerId)
            .Set(account => account.LastUpdatedDateUtc, DateTime.UtcNow)
            .Inc(account => account.Version, 1);

        if (!string.IsNullOrWhiteSpace(defaultPaymentMethodId))
        {
            update = update.Set(
                account => account.DefaultPaymentMethodId,
                defaultPaymentMethodId);
        }

        var result = await Accounts(tenantId).UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    private async Task<BillingAccount?> FindAsync(
        string tenantId,
        string organizationId,
        string providerName,
        CancellationToken cancellationToken) =>
        await Accounts(tenantId)
            .Find(Builders<BillingAccount>.Filter.And(
                Builders<BillingAccount>.Filter.Eq(
                    account => account.TenantId,
                    tenantId),
                Builders<BillingAccount>.Filter.Eq(
                    account => account.OrganizationId,
                    organizationId),
                Builders<BillingAccount>.Filter.Eq(
                    account => account.ProviderName,
                    providerName)))
            .FirstOrDefaultAsync(cancellationToken);

    private IMongoCollection<BillingAccount> Accounts(string tenantId) =>
        SubscriptionCollections.Of<BillingAccount>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.BillingAccounts);
}
