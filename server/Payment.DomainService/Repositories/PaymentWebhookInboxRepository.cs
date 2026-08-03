using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public sealed class PaymentWebhookInboxRepository : IPaymentWebhookInboxRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public PaymentWebhookInboxRepository(IDbContextProvider dbContextProvider) => _dbContextProvider = dbContextProvider;

    public async Task<WebhookStoreResult> StoreAsync(PaymentWebhookInbox webhook, CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(webhook.TenantId, cancellationToken);
        try
        {
            await Collection(webhook.TenantId).InsertOneAsync(webhook, cancellationToken: cancellationToken);
            return WebhookStoreResult.Stored;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return WebhookStoreResult.Duplicate;
        }
    }

    public Task<List<PaymentWebhookInbox>> GetDueAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentWebhookInbox>.Filter.And(
            Builders<PaymentWebhookInbox>.Filter.In(x => x.Status, [PaymentWebhookStatus.Pending, PaymentWebhookStatus.RetryScheduled, PaymentWebhookStatus.Processing]),
            Builders<PaymentWebhookInbox>.Filter.Lte(x => x.NextAttemptAtUtc, utcNow),
            Builders<PaymentWebhookInbox>.Filter.Or(
                Builders<PaymentWebhookInbox>.Filter.Ne(x => x.Status, PaymentWebhookStatus.Processing),
                Builders<PaymentWebhookInbox>.Filter.Lte(x => x.LeaseExpiresAtUtc, utcNow)));
        return Collection(tenantId).Find(filter).SortBy(x => x.CreatedAtUtc).Limit(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken);
    }

    public Task<PaymentWebhookInbox?> TryClaimAsync(string tenantId, string webhookId, string leaseId, DateTime leaseUntilUtc, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<PaymentWebhookInbox>.Filter.And(
            Builders<PaymentWebhookInbox>.Filter.Eq(x => x.WebhookId, webhookId),
            Builders<PaymentWebhookInbox>.Filter.In(x => x.Status, [PaymentWebhookStatus.Pending, PaymentWebhookStatus.RetryScheduled, PaymentWebhookStatus.Processing]),
            Builders<PaymentWebhookInbox>.Filter.Lte(x => x.NextAttemptAtUtc, now),
            Builders<PaymentWebhookInbox>.Filter.Or(
                Builders<PaymentWebhookInbox>.Filter.Ne(x => x.Status, PaymentWebhookStatus.Processing),
                Builders<PaymentWebhookInbox>.Filter.Lte(x => x.LeaseExpiresAtUtc, now)));
        var update = Builders<PaymentWebhookInbox>.Update
            .Set(x => x.Status, PaymentWebhookStatus.Processing)
            .Set(x => x.LeaseId, leaseId)
            .Set(x => x.LeaseExpiresAtUtc, leaseUntilUtc);
        return Collection(tenantId).FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<PaymentWebhookInbox> { ReturnDocument = ReturnDocument.After }, cancellationToken)!;
    }

    public Task MarkProcessedAsync(string tenantId, string webhookId, string leaseId, CancellationToken cancellationToken) =>
        Collection(tenantId).UpdateOneAsync(
            x => x.WebhookId == webhookId && x.LeaseId == leaseId,
            Builders<PaymentWebhookInbox>.Update
                .Set(x => x.Status, PaymentWebhookStatus.Processed)
                .Set(x => x.ProcessedAtUtc, DateTime.UtcNow)
                .Set(x => x.LeaseId, null)
                .Set(x => x.LeaseExpiresAtUtc, null),
            cancellationToken: cancellationToken);

    public Task MarkFailedAsync(string tenantId, string webhookId, string leaseId, PaymentWebhookStatus status, int attempts, DateTime nextAttemptAtUtc, CancellationToken cancellationToken) =>
        Collection(tenantId).UpdateOneAsync(
            x => x.WebhookId == webhookId && x.LeaseId == leaseId,
            Builders<PaymentWebhookInbox>.Update
                .Set(x => x.Status, status)
                .Set(x => x.AttemptCount, attempts)
                .Set(x => x.NextAttemptAtUtc, nextAttemptAtUtc)
                .Set(x => x.LeaseId, null)
                .Set(x => x.LeaseExpiresAtUtc, null),
            cancellationToken: cancellationToken);

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId)) return;
        await Collection(tenantId).Indexes.CreateManyAsync([
            new CreateIndexModel<PaymentWebhookInbox>(
                Builders<PaymentWebhookInbox>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.DeduplicationKey),
                new CreateIndexOptions { Unique = true, Name = "ux_webhook_tenant_deduplication" }),
            new CreateIndexModel<PaymentWebhookInbox>(
                Builders<PaymentWebhookInbox>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.NextAttemptAtUtc).Ascending(x => x.LeaseExpiresAtUtc),
                new CreateIndexOptions { Name = "ix_webhook_due" })
        ], cancellationToken);
        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<PaymentWebhookInbox> Collection(string tenantId) =>
        _dbContextProvider.GetDatabase(RequireTenant(tenantId)).GetCollection<PaymentWebhookInbox>("PaymentWebhookInbox");
    private static string RequireTenant(string tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId) ? tenantId : throw new InvalidOperationException("A tenant id is required.");
}
