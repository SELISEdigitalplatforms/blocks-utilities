using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public sealed class BillingAccountRepository : IBillingAccountRepository
{
    /// <summary>Mongo's duplicate-key code, as it arrives on a losing upsert.</summary>
    private const int DuplicateKeyErrorCode = 11000;

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

    public async Task<BillingAccount> GetOrCreateAndReconcileAsync(
        BillingAccount account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        await EnsureIndexesAsync(account.TenantId, cancellationToken);

        var identity = Builders<BillingAccount>.Filter.And(
            Builders<BillingAccount>.Filter.Eq(stored => stored.TenantId, account.TenantId),
            Builders<BillingAccount>.Filter.Eq(
                stored => stored.OrganizationId,
                account.OrganizationId),
            Builders<BillingAccount>.Filter.Eq(
                stored => stored.ProviderName,
                account.ProviderName));

        try
        {
            return await Accounts(account.TenantId).FindOneAndUpdateAsync(
                identity,
                Reconciliation(account),
                new FindOneAndUpdateOptions<BillingAccount>
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsDuplicateKey(exception))
        {
            // Two upserts raced and both decided to insert; one lost on the unique index. Its own
            // reconciliation is gone, but the winner was reconciling to the same values, so reading
            // what it wrote is the same answer this call would have given.
            return await FindAsync(
                       account.TenantId,
                       account.OrganizationId,
                       account.ProviderName,
                       cancellationToken)
                   ?? account;
        }
    }

    /// <summary>
    /// Whether a failed upsert lost a race on the unique index, rather than failing for real.
    /// </summary>
    /// <remarks>
    /// Both driver shapes are accepted deliberately. A duplicate key reaches an ordinary write as a
    /// <see cref="MongoWriteException"/> and a <c>findAndModify</c> as a
    /// <see cref="MongoCommandException"/>, and which one an upsert produces has moved between driver
    /// versions. Recognising only one would turn a lost race - the ordinary outcome of two people
    /// subscribing at once, and the case the retry below exists for - into an unhandled exception on
    /// a signup, on an upgrade nobody linked to it.
    /// </remarks>
    private static bool IsDuplicateKey(Exception exception) =>
        exception switch
        {
            MongoWriteException write =>
                write.WriteError?.Category == ServerErrorCategory.DuplicateKey,
            MongoCommandException command => command.Code == DuplicateKeyErrorCode,
            MongoBulkWriteException bulk =>
                bulk.WriteErrors.Any(error => error.Code == DuplicateKeyErrorCode),
            _ => false
        };

    /// <summary>
    /// The update that creates the account, or brings an existing one up to date.
    /// </summary>
    /// <remarks>
    /// Identity and the creation stamp go under <c>$setOnInsert</c>, so a second signup cannot
    /// rewrite the id a subscription already points at or move the creation date.
    /// <para>
    /// A contact field is set only when there is a value, which is what makes a null mean "leave it
    /// alone" rather than "blank it". Mongo refuses a field named by both operators, so anything
    /// reconciled here is deliberately absent from the insert-only list — and <c>$inc</c> on a
    /// missing field creates it, so a freshly inserted document still comes out at version 1.
    /// </para>
    /// <para>
    /// With nothing to reconcile the whole update is insert-only and an existing account is left
    /// exactly as it was: touching its timestamp to record that nothing changed would be a lie a
    /// support conversation later has to unpick.
    /// </para>
    /// </remarks>
    private static UpdateDefinition<BillingAccount> Reconciliation(BillingAccount account)
    {
        var update = Builders<BillingAccount>.Update
            .SetOnInsert(stored => stored.ItemId, account.ItemId)
            .SetOnInsert(stored => stored.TenantId, account.TenantId)
            .SetOnInsert(stored => stored.OrganizationId, account.OrganizationId)
            .SetOnInsert(stored => stored.ProviderName, account.ProviderName)
            .SetOnInsert(stored => stored.CreatedAtUtc, account.CreatedAtUtc);

        var contact = new List<UpdateDefinition<BillingAccount>>(2);

        if (!string.IsNullOrWhiteSpace(account.BillingEmail))
        {
            contact.Add(Builders<BillingAccount>.Update.Set(
                stored => stored.BillingEmail,
                account.BillingEmail));
        }

        if (!string.IsNullOrWhiteSpace(account.BillingName))
        {
            contact.Add(Builders<BillingAccount>.Update.Set(
                stored => stored.BillingName,
                account.BillingName));
        }

        if (contact.Count == 0)
        {
            return Builders<BillingAccount>.Update.Combine(
                update,
                Builders<BillingAccount>.Update.SetOnInsert(
                    stored => stored.LastUpdatedDateUtc,
                    account.LastUpdatedDateUtc),
                Builders<BillingAccount>.Update.SetOnInsert(stored => stored.Version, 1));
        }

        return Builders<BillingAccount>.Update.Combine(
            update,
            Builders<BillingAccount>.Update.Combine(contact),
            Builders<BillingAccount>.Update.Set(
                stored => stored.LastUpdatedDateUtc,
                DateTime.UtcNow),
            Builders<BillingAccount>.Update.Inc(stored => stored.Version, 1));
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
