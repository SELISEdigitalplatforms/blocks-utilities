using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

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

    public async Task<SetProviderCustomerOutcome> TrySetProviderCustomerAsync(
        string tenantId,
        string billingAccountId,
        string providerCustomerId,
        string? defaultPaymentMethodId,
        string? providerOrganizationId,
        CancellationToken cancellationToken)
    {
        // Identity only. The customer this account already names is deliberately not part of
        // the filter: a charge that confirmed against a different one is the charge that holds
        // the card a renewal will present, so it is the one worth keeping. See the interface
        // for why refusing was the more damaging option.
        var filter = Builders<BillingAccount>.Filter.And(
            Builders<BillingAccount>.Filter.Eq(
                account => account.TenantId,
                tenantId),
            Builders<BillingAccount>.Filter.Eq(
                account => account.ItemId,
                billingAccountId));

        var existing = await Accounts(tenantId)
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            return SetProviderCustomerOutcome.AccountMissing;
        }

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

        if (!string.IsNullOrWhiteSpace(providerOrganizationId))
        {
            update = update.Set(
                account => account.ProviderOrganizationId,
                providerOrganizationId);
        }

        await Accounts(tenantId).UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        // Read from what was there before the write, not from the write's own modified count:
        // an account already naming this customer but carrying a stale card is still a change
        // worth making, and one naming a different customer is worth saying out loud even when
        // every other field happened to match.
        if (string.IsNullOrWhiteSpace(existing.ProviderCustomerId))
        {
            return SetProviderCustomerOutcome.Recorded;
        }

        return string.Equals(
            existing.ProviderCustomerId,
            providerCustomerId,
            StringComparison.Ordinal)
            ? SetProviderCustomerOutcome.Unchanged
            : SetProviderCustomerOutcome.Repointed;
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
