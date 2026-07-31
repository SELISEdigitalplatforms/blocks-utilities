using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Services;

namespace Payment.DomainService.Repositories;

public sealed class StoredPaymentMethodRepository : IStoredPaymentMethodRepository
{
    /// <summary>
    /// Removals still in flight, which a new save waits behind.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes RemovalRequiresAttention. That state means the removal exhausted
    /// its retries and was given up on, so it is never going to resolve by itself. Counting it
    /// here blocked the shopper from ever saving a card again over a failure that was ours,
    /// with no way out but editing the database. The record still shows as needing attention
    /// for whoever handles it.
    /// </remarks>
    private static readonly PaymentMethodStatus[] UnresolvedRemovalStatuses =
    [
        PaymentMethodStatus.RemovalPending,
        PaymentMethodStatus.RemovalOutcomeUnknown
    ];

    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public StoredPaymentMethodRepository(
        IDbContextProvider dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public Task<List<StoredPaymentMethod>> ListActiveAsync(
        string tenantId,
        IReadOnlyCollection<string> shopperReferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shopperReferences);

        if (shopperReferences.Count == 0)
        {
            return Task.FromResult(new List<StoredPaymentMethod>());
        }

        var filter = Builders<StoredPaymentMethod>.Filter.And(
            Builders<StoredPaymentMethod>.Filter.Eq(
                method => method.TenantId,
                tenantId),
            Builders<StoredPaymentMethod>.Filter.In(
                method => method.ShopperReference,
                shopperReferences),
            Builders<StoredPaymentMethod>.Filter.Eq(
                method => method.Status,
                PaymentMethodStatus.Active));

        return Collection(tenantId)
            .Find(filter)
            .SortByDescending(method => method.UpdatedAtUtc)
            .Limit(200)
            .ToListAsync(cancellationToken);
    }

    public Task<StoredPaymentMethod?> GetAsync(
        string tenantId,
        string itemId,
        CancellationToken cancellationToken) =>
        Collection(tenantId)
            .Find(method =>
                method.TenantId == tenantId &&
                method.ItemId == itemId)
            .FirstOrDefaultAsync(cancellationToken)!;

    public Task<StoredPaymentMethod?>
        GetByTokenFingerprintAsync(
            string tenantId,
            string shopperReference,
            string providerName,
            string tokenFingerprint,
            CancellationToken cancellationToken) =>
        Collection(tenantId)
            .Find(method =>
                method.TenantId == tenantId &&
                method.ShopperReference == shopperReference &&
                method.ProviderName == providerName &&
                method.ProviderTokenFingerprint ==
                tokenFingerprint)
            .FirstOrDefaultAsync(cancellationToken)!;

    public Task<StoredPaymentMethod?> GetByCardFingerprintAsync(
        string tenantId,
        string? organizationId,
        string shopperReference,
        string providerName,
        string cardFingerprint,
        CancellationToken cancellationToken) =>
        Collection(tenantId)
            .Find(method =>
                method.TenantId == tenantId &&
                // The same card saved at two organizations is two cards: each is a distinct
                // token at a distinct merchant account, and neither can charge the other's.
                method.OrganizationId == organizationId &&
                method.ShopperReference == shopperReference &&
                method.ProviderName == providerName &&
                method.ProviderCardFingerprint == cardFingerprint &&
                method.Status == PaymentMethodStatus.Active)
            .FirstOrDefaultAsync(cancellationToken)!;

    public async Task<bool> SupersedeTokenAsync(
        StoredPaymentMethod method,
        DateTime eventDateUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);

        var result = await Collection(method.TenantId).UpdateOneAsync(
            candidate =>
                candidate.TenantId == method.TenantId &&
                candidate.ItemId == method.ItemId &&
                candidate.Status == PaymentMethodStatus.Active,
            Builders<StoredPaymentMethod>.Update
                .Set(candidate => candidate.ProviderTokenCiphertext, method.ProviderTokenCiphertext)
                .Set(candidate => candidate.ProviderTokenFingerprint, method.ProviderTokenFingerprint)
                .Set(candidate => candidate.TokenEncryptionKeyId, method.TokenEncryptionKeyId)
                .Set(candidate => candidate.ProviderPayerReference, method.ProviderPayerReference)
                .Set(candidate => candidate.Brand, method.Brand)
                .Set(candidate => candidate.LastFour, method.LastFour)
                .Set(candidate => candidate.ExpiryMonth, method.ExpiryMonth)
                .Set(candidate => candidate.ExpiryYear, method.ExpiryYear)
                .Set(candidate => candidate.FundingSource, method.FundingSource)
                .Set(candidate => candidate.IssuerCountry, method.IssuerCountry)
                .Set(candidate => candidate.LastProviderEventAtUtc, eventDateUtc)
                .Set(candidate => candidate.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public Task<bool> HasUnresolvedRemovalAsync(
        string tenantId,
        string shopperReference,
        CancellationToken cancellationToken) =>
        Collection(tenantId)
            .Find(method =>
                method.TenantId == tenantId &&
                method.ShopperReference == shopperReference &&
                UnresolvedRemovalStatuses.Contains(method.Status))
            .Limit(1)
            .AnyAsync(cancellationToken);

    public async Task UpsertFromProviderAsync(
        StoredPaymentMethod method,
        DateTime eventDateUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(
            method.TenantId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(
                method.ProviderTokenFingerprint))
        {
            throw new InvalidOperationException(
                "A provider token fingerprint is required.");
        }

        if (method.Status == PaymentMethodStatus.Removed)
        {
            await MarkRemovedFromProviderAsync(
                method.TenantId,
                method.ShopperReference,
                method.ProviderTokenFingerprint,
                eventDateUtc,
                cancellationToken);

            return;
        }

        var filter = Builders<StoredPaymentMethod>.Filter.And(
            Builders<StoredPaymentMethod>.Filter.Eq(
                candidate => candidate.TenantId,
                method.TenantId),
            Builders<StoredPaymentMethod>.Filter.Eq(
                candidate => candidate.ShopperReference,
                method.ShopperReference),
            Builders<StoredPaymentMethod>.Filter.Eq(
                candidate => candidate.ProviderName,
                method.ProviderName),
            Builders<StoredPaymentMethod>.Filter.Eq(
                candidate => candidate.ProviderTokenFingerprint,
                method.ProviderTokenFingerprint),
            Builders<StoredPaymentMethod>.Filter.Or(
                Builders<StoredPaymentMethod>.Filter.Exists(
                    candidate => candidate.Status,
                    false),
                Builders<StoredPaymentMethod>.Filter.Eq(
                    candidate => candidate.Status,
                    PaymentMethodStatus.Active)),
            Builders<StoredPaymentMethod>.Filter.Or(
                Builders<StoredPaymentMethod>.Filter.Exists(
                    candidate => candidate.LastProviderEventAtUtc,
                    false),
                Builders<StoredPaymentMethod>.Filter.Lte(
                    candidate => candidate.LastProviderEventAtUtc,
                    eventDateUtc)));

        var update = Builders<StoredPaymentMethod>.Update
            .SetOnInsert(candidate => candidate.ItemId, method.ItemId)
            .SetOnInsert(candidate => candidate.CreatedAtUtc, method.CreatedAtUtc)
            .Set(candidate => candidate.TenantId, method.TenantId)
            .Set(candidate => candidate.ShopperReference, method.ShopperReference)
            .Set(candidate => candidate.ProviderName, method.ProviderName)
            .Set(candidate => candidate.ProviderTokenCiphertext, method.ProviderTokenCiphertext)
            .Set(candidate => candidate.ProviderTokenFingerprint, method.ProviderTokenFingerprint)
            .Set(candidate => candidate.TokenEncryptionKeyId, method.TokenEncryptionKeyId)
            .Set(candidate => candidate.ProviderPayerReference, method.ProviderPayerReference)
            .Set(candidate => candidate.ProviderCardFingerprint, method.ProviderCardFingerprint)
            .Set(candidate => candidate.OrganizationId, method.OrganizationId)
            .Unset(candidate => candidate.StoredPaymentMethodToken)
            .Set(candidate => candidate.Type, method.Type)
            .Set(candidate => candidate.Brand, method.Brand)
            .Set(candidate => candidate.LastFour, method.LastFour)
            .Set(candidate => candidate.ExpiryMonth, method.ExpiryMonth)
            .Set(candidate => candidate.ExpiryYear, method.ExpiryYear)
            .Set(candidate => candidate.FundingSource, method.FundingSource)
            .Set(candidate => candidate.IssuerCountry, method.IssuerCountry)
            .Set(candidate => candidate.Status, PaymentMethodStatus.Active)
            .Set(candidate => candidate.LastProviderEventAtUtc, eventDateUtc)
            .Set(candidate => candidate.UpdatedAtUtc, DateTime.UtcNow);

        try
        {
            await Collection(method.TenantId)
                .UpdateOneAsync(
                    filter,
                    update,
                    new UpdateOptions
                    {
                        IsUpsert = true
                    },
                    cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category ==
                  ServerErrorCategory.DuplicateKey)
        {
            // A newer event or a local removal owns the token record.
        }
    }

    public async Task<bool> ReactivateAfterFreshConsentAsync(
        StoredPaymentMethod method,
        DateTime paymentCreatedAtUtc,
        DateTime eventDateUtc,
        CancellationToken cancellationToken)
    {
        var result = await Collection(method.TenantId)
            .UpdateOneAsync(
                candidate =>
                    candidate.TenantId == method.TenantId &&
                    candidate.ShopperReference ==
                    method.ShopperReference &&
                    candidate.ProviderName ==
                    method.ProviderName &&
                    candidate.ProviderTokenFingerprint ==
                    method.ProviderTokenFingerprint &&
                    candidate.Status ==
                    PaymentMethodStatus.Removed &&
                    candidate.RemovedAtUtc <
                    paymentCreatedAtUtc,
                Builders<StoredPaymentMethod>.Update
                    .Set(candidate => candidate.Status, PaymentMethodStatus.Active)
                    .Set(candidate => candidate.ProviderTokenCiphertext, method.ProviderTokenCiphertext)
                    .Set(candidate => candidate.TokenEncryptionKeyId, method.TokenEncryptionKeyId)
                    .Unset(candidate => candidate.StoredPaymentMethodToken)
                    .Set(candidate => candidate.Type, method.Type)
                    .Set(candidate => candidate.Brand, method.Brand)
                    .Set(candidate => candidate.LastFour, method.LastFour)
                    .Set(candidate => candidate.ExpiryMonth, method.ExpiryMonth)
                    .Set(candidate => candidate.ExpiryYear, method.ExpiryYear)
                    .Set(candidate => candidate.FundingSource, method.FundingSource)
                    .Set(candidate => candidate.IssuerCountry, method.IssuerCountry)
                    .Set(candidate => candidate.RemovedAtUtc, null)
                    .Set(candidate => candidate.LastRemovalErrorCode, null)
                    .Set(candidate => candidate.RemovalAttemptCount, 0)
                    .Set(candidate => candidate.LastProviderEventAtUtc, eventDateUtc)
                    .Set(candidate => candidate.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<StoredPaymentMethod?> TryClaimRemovalAsync(
        string tenantId,
        string itemId,
        string shopperReference,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(
            tenantId,
            cancellationToken);

        var now = DateTime.UtcNow;
        var update = Builders<StoredPaymentMethod>.Update
            .Set(method => method.Status, PaymentMethodStatus.RemovalPending)
            .Set(method => method.RemovalLeaseId, leaseId)
            .Set(method => method.RemovalLeaseExpiresAtUtc, leaseExpiresAtUtc)
            .Set(method => method.NextRemovalAttemptAtUtc, now)
            .Set(method => method.LastRemovalErrorCode, null)
            .Set(method => method.UpdatedAtUtc, now);

        return await Collection(tenantId)
            .FindOneAndUpdateAsync(
                method =>
                    method.TenantId == tenantId &&
                    method.ItemId == itemId &&
                    method.ShopperReference == shopperReference &&
                    method.Status == PaymentMethodStatus.Active &&
                    (method.PaymentUseLeaseExpiresAtUtc == null ||
                     method.PaymentUseLeaseExpiresAtUtc <= now),
                update,
                new FindOneAndUpdateOptions<
                    StoredPaymentMethod,
                    StoredPaymentMethod>
                {
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);
    }

    public async Task<StoredPaymentMethod?> TryClaimForPaymentAsync(
        string tenantId,
        string itemId,
        string shopperReference,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<StoredPaymentMethod>.Filter.And(
            Builders<StoredPaymentMethod>.Filter.Eq(
                method => method.TenantId,
                tenantId),
            Builders<StoredPaymentMethod>.Filter.Eq(
                method => method.ItemId,
                itemId),
            Builders<StoredPaymentMethod>.Filter.Eq(
                method => method.ShopperReference,
                shopperReference),
            Builders<StoredPaymentMethod>.Filter.Eq(
                method => method.Status,
                PaymentMethodStatus.Active),
            Builders<StoredPaymentMethod>.Filter.Or(
                Builders<StoredPaymentMethod>.Filter.Eq(
                    method => method.PaymentUseLeaseExpiresAtUtc,
                    null),
                Builders<StoredPaymentMethod>.Filter.Lte(
                    method => method.PaymentUseLeaseExpiresAtUtc,
                    now)));
        var update = Builders<StoredPaymentMethod>.Update
            .Set(
                method => method.PaymentUseLeaseId,
                leaseId)
            .Set(
                method => method.PaymentUseLeaseExpiresAtUtc,
                leaseExpiresAtUtc)
            .Set(
                method => method.UpdatedAtUtc,
                now);

        return await Collection(tenantId)
            .FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<
                    StoredPaymentMethod,
                    StoredPaymentMethod>
                {
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);
    }

    public Task ReleasePaymentClaimAsync(
        string tenantId,
        string itemId,
        string leaseId,
        CancellationToken cancellationToken) =>
        Collection(tenantId).UpdateOneAsync(
            method =>
                method.TenantId == tenantId &&
                method.ItemId == itemId &&
                method.PaymentUseLeaseId == leaseId,
            Builders<StoredPaymentMethod>.Update
                .Set(
                    method => method.PaymentUseLeaseId,
                    null)
                .Set(
                    method => method.PaymentUseLeaseExpiresAtUtc,
                    null)
                .Set(
                    method => method.UpdatedAtUtc,
                    DateTime.UtcNow),
            cancellationToken: cancellationToken);

    public Task<List<StoredPaymentMethod>> GetDueRemovalCandidatesAsync(
        string tenantId,
        DateTime utcNow,
        int limit,
        CancellationToken cancellationToken) =>
        Collection(tenantId)
            .Find(method =>
                method.TenantId == tenantId &&
                (method.Status == PaymentMethodStatus.RemovalPending ||
                 method.Status == PaymentMethodStatus.RemovalOutcomeUnknown) &&
                method.NextRemovalAttemptAtUtc <= utcNow &&
                (method.RemovalLeaseExpiresAtUtc == null ||
                 method.RemovalLeaseExpiresAtUtc <= utcNow))
            .SortBy(method => method.NextRemovalAttemptAtUtc)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

    public async Task<StoredPaymentMethod?> TryClaimDueRemovalAsync(
        string tenantId,
        string itemId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var update = Builders<StoredPaymentMethod>.Update
            .Set(method => method.RemovalLeaseId, leaseId)
            .Set(method => method.RemovalLeaseExpiresAtUtc, leaseExpiresAtUtc)
            .Set(method => method.UpdatedAtUtc, utcNow);

        return await Collection(tenantId)
            .FindOneAndUpdateAsync(
                method =>
                    method.TenantId == tenantId &&
                    method.ItemId == itemId &&
                    (method.Status == PaymentMethodStatus.RemovalPending ||
                     method.Status == PaymentMethodStatus.RemovalOutcomeUnknown) &&
                    method.NextRemovalAttemptAtUtc <= utcNow &&
                    (method.RemovalLeaseExpiresAtUtc == null ||
                     method.RemovalLeaseExpiresAtUtc <= utcNow),
                update,
                new FindOneAndUpdateOptions<
                    StoredPaymentMethod,
                    StoredPaymentMethod>
                {
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);
    }

    public async Task<bool> MarkRemovalOutcomeUnknownAsync(
        string tenantId,
        string itemId,
        string leaseId,
        DateTime nextAttemptAtUtc,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var result = await Collection(tenantId)
            .UpdateOneAsync(
                method =>
                    method.TenantId == tenantId &&
                    method.ItemId == itemId &&
                    method.RemovalLeaseId == leaseId,
                Builders<StoredPaymentMethod>.Update
                    .Set(method => method.Status, PaymentMethodStatus.RemovalOutcomeUnknown)
                    .Set(method => method.NextRemovalAttemptAtUtc, nextAttemptAtUtc)
                    .Set(method => method.LastRemovalErrorCode, errorCode)
                    .Set(method => method.RemovalLeaseId, null)
                    .Set(method => method.RemovalLeaseExpiresAtUtc, null)
                    .Inc(method => method.RemovalAttemptCount, 1)
                    .Set(method => method.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> MarkRemovedAsync(
        string tenantId,
        string itemId,
        string leaseId,
        DateTime removedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Collection(tenantId)
            .UpdateOneAsync(
                method =>
                    method.TenantId == tenantId &&
                    method.ItemId == itemId &&
                    method.RemovalLeaseId == leaseId,
                Builders<StoredPaymentMethod>.Update
                    .Set(method => method.Status, PaymentMethodStatus.Removed)
                    .Set(method => method.RemovedAtUtc, removedAtUtc)
                    .Set(method => method.LastProviderEventAtUtc, removedAtUtc)
                    .Set(method => method.NextRemovalAttemptAtUtc, null)
                    .Set(method => method.LastRemovalErrorCode, null)
                    .Set(method => method.RemovalLeaseId, null)
                    .Set(method => method.RemovalLeaseExpiresAtUtc, null)
                    .Set(method => method.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public Task MarkRemovedFromProviderAsync(
        string tenantId,
        string shopperReference,
        string tokenFingerprint,
        DateTime eventDateUtc,
        CancellationToken cancellationToken) =>
        Collection(tenantId)
            .UpdateOneAsync(
                method =>
                    method.TenantId == tenantId &&
                    method.ShopperReference == shopperReference &&
                    method.ProviderTokenFingerprint == tokenFingerprint &&
                    method.LastProviderEventAtUtc <= eventDateUtc,
                Builders<StoredPaymentMethod>.Update
                    .Set(method => method.Status, PaymentMethodStatus.Removed)
                    .Set(method => method.RemovedAtUtc, eventDateUtc)
                    .Set(method => method.LastProviderEventAtUtc, eventDateUtc)
                    .Set(method => method.NextRemovalAttemptAtUtc, null)
                    .Set(method => method.LastRemovalErrorCode, null)
                    .Set(method => method.RemovalLeaseId, null)
                    .Set(method => method.RemovalLeaseExpiresAtUtc, null)
                    .Set(method => method.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken: cancellationToken);

    public async Task<bool> MarkRemovalRequiresAttentionAsync(
        string tenantId,
        string itemId,
        string leaseId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var result = await Collection(tenantId)
            .UpdateOneAsync(
                method =>
                    method.TenantId == tenantId &&
                    method.ItemId == itemId &&
                    method.RemovalLeaseId == leaseId,
                Builders<StoredPaymentMethod>.Update
                    .Set(method => method.Status, PaymentMethodStatus.RemovalRequiresAttention)
                    .Set(method => method.LastRemovalErrorCode, errorCode)
                    .Set(method => method.NextRemovalAttemptAtUtc, null)
                    .Set(method => method.RemovalLeaseId, null)
                    .Set(method => method.RemovalLeaseExpiresAtUtc, null)
                    .Inc(method => method.RemovalAttemptCount, 1)
                    .Set(method => method.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public Task MigrateLegacyTokenAsync(
        string tenantId,
        string itemId,
        ProtectedProviderToken protectedToken,
        CancellationToken cancellationToken) =>
        Collection(tenantId)
            .UpdateOneAsync(
                method =>
                    method.TenantId == tenantId &&
                    method.ItemId == itemId &&
                    method.ProviderTokenCiphertext == null,
                Builders<StoredPaymentMethod>.Update
                    .Set(method => method.ProviderTokenCiphertext, protectedToken.Ciphertext)
                    .Set(method => method.ProviderTokenFingerprint, protectedToken.Fingerprint)
                    .Set(method => method.TokenEncryptionKeyId, protectedToken.EncryptionKeyId)
                    .Unset(method => method.StoredPaymentMethodToken)
                    .Set(method => method.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken: cancellationToken);

    private async Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId))
        {
            return;
        }

        var collection = Collection(tenantId);

        try
        {
            await collection.Indexes.DropOneAsync(
                "ux_method_tenant_shopper_token",
                cancellationToken);
        }
        catch (MongoCommandException exception)
            when (exception.Code == 27 ||
                  exception.CodeName == "IndexNotFound")
        {
            // Older tenant databases may never have created the legacy index.
        }

        await collection.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<StoredPaymentMethod>(
                Builders<StoredPaymentMethod>.IndexKeys
                    .Ascending(method => method.TenantId)
                    .Ascending(method => method.ShopperReference)
                    .Ascending(method => method.ProviderName)
                    .Ascending(method => method.ProviderTokenFingerprint),
                new CreateIndexOptions<StoredPaymentMethod>
                {
                    Unique = true,
                    Name = "ux_method_tenant_shopper_provider_token",
                    PartialFilterExpression =
                        new BsonDocument(
                            nameof(StoredPaymentMethod.ProviderTokenFingerprint),
                            new BsonDocument("$type", "string"))
                }),
            new CreateIndexModel<StoredPaymentMethod>(
                Builders<StoredPaymentMethod>.IndexKeys
                    .Ascending(method => method.TenantId)
                    .Ascending(method => method.ItemId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "ux_method_tenant_item"
                }),
            new CreateIndexModel<StoredPaymentMethod>(
                Builders<StoredPaymentMethod>.IndexKeys
                    .Ascending(method => method.ShopperReference)
                    .Ascending(method => method.Status),
                new CreateIndexOptions
                {
                    Name = "ix_method_shopper_status"
                }),
            new CreateIndexModel<StoredPaymentMethod>(
                Builders<StoredPaymentMethod>.IndexKeys
                    .Ascending(method => method.Status)
                    .Ascending(method => method.NextRemovalAttemptAtUtc)
                    .Ascending(method => method.RemovalLeaseExpiresAtUtc),
                new CreateIndexOptions
                {
                    Name = "ix_method_removal_due"
                })
        ],
        cancellationToken);

        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<StoredPaymentMethod> Collection(
        string tenantId) =>
        _dbContextProvider
            .GetDatabase(tenantId)
            .GetCollection<StoredPaymentMethod>(
                "StoredPaymentMethods");
}
