using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public sealed class BillingAccountRepository : IBillingAccountRepository
{
    /// <summary>Mongo's duplicate-key code, as it arrives on a losing upsert.</summary>
    private const int DuplicateKeyErrorCode = 11000;

    // Stored element names, which this entity maps from its property names. Only the id is renamed,
    // and nothing here touches it. Were that to change, the update below would name a field twice
    // and Mongo would refuse it outright rather than write something surprising.
    private const string BillingEmailField = nameof(BillingAccount.BillingEmail);
    private const string BillingNameField = nameof(BillingAccount.BillingName);
    private const string LastUpdatedField = nameof(BillingAccount.LastUpdatedDateUtc);
    private const string VersionField = nameof(BillingAccount.Version);

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

        BillingAccount result;

        try
        {
            result = await Accounts(account.TenantId).FindOneAndUpdateAsync(
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
            result = await FindAsync(
                         account.TenantId,
                         account.OrganizationId,
                         account.ProviderName,
                         cancellationToken)
                     ?? account;
        }

        return await BackfillProviderIdentityAsync(account, result, cancellationToken);
    }

    /// <summary>
    /// One-time self-healing backfill of <see cref="BillingAccount.ProviderId"/> and
    /// <see cref="BillingAccount.ProviderOrganizationId"/> onto a legacy account that predates
    /// this PR's provider-identity work and so was created with both left null.
    /// </summary>
    /// <remarks>
    /// Deliberately not folded into <see cref="Reconciliation"/>: that single find-and-modify
    /// cannot express "write this field only if it is currently null" — <c>$set</c> and
    /// <c>$setOnInsert</c> both act unconditionally on their own side of insert-vs-update — so the
    /// conditional write here follows this codebase's established compare-and-set convention
    /// instead (see <c>PaymentRepository.TryRecordSetupTokenConfirmedAsync</c> in
    /// Payment.DomainService): a conditional update filtered on the field still being null.
    /// That filter is what makes
    /// this strictly additive and one-directional — a billing account's provider identity is
    /// frozen once set, and this must never be the thing that silently moves it, only the thing
    /// that fills in a value legacy accounts were never given a chance to record.
    /// <para>
    /// Uses <c>FindOneAndUpdate</c> with <see cref="ReturnDocument.After"/> rather than an
    /// <c>UpdateOneAsync</c> read off its <c>ModifiedCount</c>, so a losing writer in a race
    /// between two concurrent backfills of the same legacy account still gets back whatever is
    /// actually on the document now -- the value the winner wrote -- instead of the stale
    /// pre-update <paramref name="stored"/> it was handed, which would still show
    /// <c>ProviderId == null</c> and cause its caller to skip the fail-closed
    /// <c>ExpectedProviderId</c> check even though the database already has a frozen identity.
    /// See PR #393 review (Finding 2).
    /// </para>
    /// </remarks>
    private async Task<BillingAccount> BackfillProviderIdentityAsync(
        BillingAccount account,
        BillingAccount stored,
        CancellationToken cancellationToken)
    {
        if (stored.ProviderId is not null || account.ProviderId is null)
        {
            // Either this account's provider identity is already frozen (nothing to backfill),
            // or the caller has no provider identity to offer -- e.g. a reconcile call that
            // predates this PR's provider-identity work reaching this code path.
            return stored;
        }

        var filter = Builders<BillingAccount>.Filter.And(
            Builders<BillingAccount>.Filter.Eq(x => x.TenantId, stored.TenantId),
            Builders<BillingAccount>.Filter.Eq(x => x.ItemId, stored.ItemId),
            Builders<BillingAccount>.Filter.Eq(x => x.ProviderId, null));

        var update = Builders<BillingAccount>.Update
            .Set(x => x.ProviderId, account.ProviderId)
            .Set(x => x.ProviderOrganizationId, account.ProviderOrganizationId)
            .Set(x => x.LastUpdatedDateUtc, DateTime.UtcNow);

        var updated = await Accounts(stored.TenantId).FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<BillingAccount> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        if (updated is not null)
        {
            // This call's filter matched, so it is the one -- whether uncontested or the winner
            // of a race -- whose write the returned document reflects.
            return updated;
        }

        // The filter no longer matched: another concurrent call already backfilled this account
        // between the read that produced `stored` and this attempt. Re-read rather than return
        // the stale `stored` object, so every concurrent caller ends up agreeing on the same
        // winning, non-null ProviderId rather than the loser silently reporting none at all.
        return await FindAsync(
                   stored.TenantId,
                   stored.OrganizationId,
                   stored.ProviderName,
                   cancellationToken)
               ?? stored;
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
        // The whole argument under $setOnInsert, rather than a hand-written list of its fields.
        // Listing them by hand is what the first version of this did, and it silently dropped the
        // provider customer id and the saved card on insert — which surfaces as a renewal with no
        // card to present, a long way from the cause. Serialising the entity means a field added to
        // it later is inserted without anybody having to remember this method exists.
        var insert = account.ToBsonDocument();
        var reconciled = new BsonDocument();

        if (!string.IsNullOrWhiteSpace(account.BillingEmail))
        {
            reconciled[BillingEmailField] = account.BillingEmail;
        }

        if (!string.IsNullOrWhiteSpace(account.BillingName))
        {
            reconciled[BillingNameField] = account.BillingName;
        }

        if (reconciled.ElementCount == 0)
        {
            // Nothing to bring up to date, so an existing account is left exactly as it stands,
            // timestamp included: recording that nothing changed would be a lie a support
            // conversation later has to unpick.
            return new BsonDocument("$setOnInsert", insert);
        }

        reconciled[LastUpdatedField] = DateTime.UtcNow;

        // Mongo refuses a field named by two operators, so whatever is being written now comes out
        // of the insert-only half — it is being written on both paths anyway. $inc creates a
        // missing field, so a freshly inserted document still lands on version 1.
        foreach (var name in reconciled.Names.ToList())
        {
            insert.Remove(name);
        }

        insert.Remove(VersionField);

        return new BsonDocument
        {
            { "$setOnInsert", insert },
            { "$set", reconciled },
            { "$inc", new BsonDocument(VersionField, 1) }
        };
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
