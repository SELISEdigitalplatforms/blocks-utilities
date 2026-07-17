using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public PaymentRepository(IDbContextProvider dbContextProvider) => _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId)) return;
        var payments = Payments(tenantId);
        await payments.Indexes.CreateManyAsync(
            PaymentIndexDefinitions.Create(),
            cancellationToken);
        await DropLegacyOutboxDeduplicationIndexAsync(
            payments,
            cancellationToken);
        _indexedTenants.TryAdd(tenantId, 0);
    }

    private static async Task DropLegacyOutboxDeduplicationIndexAsync(
        IMongoCollection<PaymentDetail> payments,
        CancellationToken cancellationToken)
    {
        try
        {
            await payments.Indexes.DropOneAsync(
                PaymentIndexDefinitions.LegacyOutboxDeduplicationIndexName,
                cancellationToken);
        }
        catch (MongoCommandException exception)
            when (exception.Code == 27 ||
                  string.Equals(
                      exception.CodeName,
                      "IndexNotFound",
                      StringComparison.OrdinalIgnoreCase))
        {
            // The legacy index has already been removed for this tenant.
        }
    }

    public async Task<PaymentProvider?> GetProviderAsync(string tenantId, string providerName, CancellationToken cancellationToken)
    {
        var normalized = providerName.Trim();
        var filter = Builders<PaymentProvider>.Filter.And(
            Builders<PaymentProvider>.Filter.Regex(
                x => x.ProviderName,
                new BsonRegularExpression(
                    $"^{System.Text.RegularExpressions.Regex.Escape(normalized)}$",
                    "i")),
            Builders<PaymentProvider>.Filter.Eq(
                x => x.IsEnabled,
                true));

        return await Providers(tenantId).Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryCreateAsync(PaymentDetail payment, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureIndexesAsync(payment.TenantId, cancellationToken);
            await Payments(payment.TenantId).InsertOneAsync(payment, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public Task<PaymentDetail?> GetByIdAsync(string tenantId, string paymentId, CancellationToken cancellationToken) =>
        Payments(tenantId).Find(x => x.ItemId == paymentId && x.TenantId == tenantId).FirstOrDefaultAsync(cancellationToken)!;

    public Task<PaymentDetail?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken) =>
        Payments(tenantId).Find(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey).FirstOrDefaultAsync(cancellationToken)!;

    public async Task<PaymentDetail?> TryClaimInitiationAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        DateTime leaseUntilUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.In(x => x.PaymentStatus, [PaymentStatuses.Initiating, PaymentStatuses.InitiationUnknown]),
            Builders<PaymentDetail>.Filter.Or(
                Builders<PaymentDetail>.Filter.Eq(x => x.ProcessingLeaseExpiresAtUtc, null),
                Builders<PaymentDetail>.Filter.Lte(x => x.ProcessingLeaseExpiresAtUtc, now)));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.ProcessingLeaseId, leaseId)
            .Set(x => x.ProcessingLeaseExpiresAtUtc, leaseUntilUtc)
            .Set(x => x.PaymentStatus, PaymentStatuses.Initiating)
            .Set(x => x.LastUpdatedDateUtc, now)
            .Inc(x => x.InitiationAttemptCount, 1);
        return await Payments(tenantId).FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<PaymentDetail> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public async Task<bool> SaveInitiationRequestAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        Payment.DomainService.Models.HostedCheckout.HostedCheckoutSessionRequest request,
        string frontendResultUrlSnapshot,
        string returnStateNonceHash,
        string shopperReference,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.ProcessingLeaseId, leaseId),
            Builders<PaymentDetail>.Filter.Eq(x => x.PaymentStatus, PaymentStatuses.Initiating));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.InitiationRequest, request)
            .Set(x => x.FrontendResultUrlSnapshot, frontendResultUrlSnapshot)
            .Set(x => x.ReturnStateNonceHash, returnStateNonceHash)
            .Set(x => x.ShopperReference, shopperReference)
            .Set(x => x.SiteId, request.Metadata.SiteId)
            .Set(x => x.LastUpdatedDateUtc, DateTime.UtcNow);
        var result = await Payments(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> CompleteInitiationAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string status,
        string? sessionId,
        string? sessionData,
        string? redirectUrl,
        DateTime? expiresAtUtc,
        string? failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.ProcessingLeaseId, leaseId),
            Builders<PaymentDetail>.Filter.Not(Builders<PaymentDetail>.Filter.ElemMatch(
                x => x.OutboxEvents, x => x.DeduplicationKey == outboxEvent.DeduplicationKey)));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.PaymentStatus, status)
            .Set(x => x.SessionId, sessionId)
            .Set(x => x.SessionData, sessionData)
            .Set(x => x.RedirectUrl, redirectUrl)
            .Set(x => x.ExpirationDate, expiresAtUtc ?? default)
            .Set(x => x.FailureCode, failureCode)
            .Set(x => x.ProcessingLeaseId, null)
            .Set(x => x.ProcessingLeaseExpiresAtUtc, null)
            .Set(x => x.LastUpdatedDateUtc, DateTime.UtcNow)
            .Push(x => x.OutboxEvents, outboxEvent);
        var result = await Payments(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task MarkInitiationUnknownAsync(string tenantId, string paymentId, string leaseId, string failureCode, CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.ProcessingLeaseId, leaseId));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.PaymentStatus, PaymentStatuses.InitiationUnknown)
            .Set(x => x.FailureCode, failureCode)
            .Set(x => x.ProcessingLeaseId, null)
            .Set(x => x.ProcessingLeaseExpiresAtUtc, null)
            .Set(x => x.LastUpdatedDateUtc, DateTime.UtcNow);
        await Payments(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    public async Task<bool> SaveCheckoutObservationAsync(
        string tenantId,
        string paymentId,
        string sessionStatus,
        string? resultCode,
        string sessionResultHash,
        string? pspReference,
        PaymentInstrument? instrument,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Nin(x => x.PaymentStatus, [PaymentStatuses.Authorized, PaymentStatuses.Refused]));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.CheckoutSessionStatus, sessionStatus)
            .Set(x => x.CheckoutResultCode, resultCode)
            .Set(x => x.CheckoutObservedAtUtc, DateTime.UtcNow)
            .Set(x => x.SessionResultHash, sessionResultHash)
            .Set(x => x.PspReference, pspReference)
            .Set(x => x.PaymentInstrument, instrument)
            .Set(x => x.LastUpdatedDateUtc, DateTime.UtcNow);
        var result = await Payments(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task<bool> ApplyAuthorisationAsync(
        string tenantId,
        string paymentId,
        bool authorized,
        string pspReference,
        DateTime eventDateUtc,
        PaymentInstrument? instrument,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Or(
                Builders<PaymentDetail>.Filter.Eq(x => x.WebhookConfirmedAtUtc, null),
                Builders<PaymentDetail>.Filter.Lte(x => x.WebhookConfirmedAtUtc, eventDateUtc)),
            Builders<PaymentDetail>.Filter.Not(Builders<PaymentDetail>.Filter.ElemMatch(
                x => x.OutboxEvents, x => x.DeduplicationKey == outboxEvent.DeduplicationKey)));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.PaymentStatus, authorized ? PaymentStatuses.Authorized : PaymentStatuses.Refused)
            .Set(x => x.PspReference, pspReference)
            .Set(x => x.WebhookConfirmedAtUtc, eventDateUtc)
            .Set(x => x.PaymentInstrument, instrument)
            .Set(x => x.LastUpdatedDateUtc, DateTime.UtcNow)
            .Push(x => x.OutboxEvents, outboxEvent);
        var result = await Payments(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<List<PaymentDetail>> GetPaymentsWithDueOutboxEventsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        var eventFilter = Builders<PaymentOutboxEvent>.Filter.And(
            Builders<PaymentOutboxEvent>.Filter.In(x => x.Status, [PaymentOutboxStatus.Pending, PaymentOutboxStatus.RetryScheduled, PaymentOutboxStatus.Processing]),
            Builders<PaymentOutboxEvent>.Filter.Lte(x => x.NextAttemptAtUtc, utcNow),
            Builders<PaymentOutboxEvent>.Filter.Or(
                Builders<PaymentOutboxEvent>.Filter.Ne(x => x.Status, PaymentOutboxStatus.Processing),
                Builders<PaymentOutboxEvent>.Filter.Lte(x => x.LeaseExpiresAtUtc, utcNow)));
        return await Payments(tenantId)
            .Find(Builders<PaymentDetail>.Filter.ElemMatch(x => x.OutboxEvents, eventFilter))
            .SortBy(x => x.LastUpdatedDateUtc)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimOutboxEventAsync(string tenantId, string paymentId, string eventId, string leaseId, DateTime leaseUntilUtc, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = new BsonDocument
        {
            ["_id"] = paymentId,
            ["TenantId"] = tenantId,
            ["OutboxEvents"] = new BsonDocument("$elemMatch", new BsonDocument
            {
                ["EventId"] = eventId,
                ["Status"] = new BsonDocument("$in", new BsonArray { (int)PaymentOutboxStatus.Pending, (int)PaymentOutboxStatus.RetryScheduled, (int)PaymentOutboxStatus.Processing }),
                ["NextAttemptAtUtc"] = new BsonDocument("$lte", now),
                ["$or"] = new BsonArray
                {
                    new BsonDocument("Status", new BsonDocument("$ne", (int)PaymentOutboxStatus.Processing)),
                    new BsonDocument("LeaseExpiresAtUtc", new BsonDocument("$lte", now))
                }
            })
        };
        var update = new BsonDocument("$set", new BsonDocument
        {
            ["OutboxEvents.$[evt].Status"] = (int)PaymentOutboxStatus.Processing,
            ["OutboxEvents.$[evt].LeaseId"] = leaseId,
            ["OutboxEvents.$[evt].LeaseExpiresAtUtc"] = leaseUntilUtc
        });
        var options = EventArrayOptions(eventId);
        var result = await Payments(tenantId).UpdateOneAsync(filter, update, options, cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task MarkOutboxPublishedAsync(string tenantId, string paymentId, string eventId, string leaseId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.ElemMatch(x => x.OutboxEvents, x => x.EventId == eventId && x.LeaseId == leaseId));
        var update = new BsonDocument("$set", new BsonDocument
        {
            ["OutboxEvents.$[evt].Status"] = (int)PaymentOutboxStatus.Published,
            ["OutboxEvents.$[evt].PublishedAtUtc"] = utcNow,
            ["OutboxEvents.$[evt].LeaseId"] = BsonNull.Value,
            ["OutboxEvents.$[evt].LeaseExpiresAtUtc"] = BsonNull.Value
        });
        await Payments(tenantId).UpdateOneAsync(filter, update, EventArrayOptions(eventId), cancellationToken);
    }

    public async Task MarkOutboxFailedAsync(
        string tenantId, string paymentId, string eventId, string leaseId, PaymentOutboxStatus status,
        int attemptCount, DateTime nextAttemptAtUtc, string error, CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.ElemMatch(x => x.OutboxEvents, x => x.EventId == eventId && x.LeaseId == leaseId));
        var update = new BsonDocument("$set", new BsonDocument
        {
            ["OutboxEvents.$[evt].Status"] = (int)status,
            ["OutboxEvents.$[evt].AttemptCount"] = attemptCount,
            ["OutboxEvents.$[evt].NextAttemptAtUtc"] = nextAttemptAtUtc,
            ["OutboxEvents.$[evt].LastError"] = Sanitize(error),
            ["OutboxEvents.$[evt].LeaseId"] = BsonNull.Value,
            ["OutboxEvents.$[evt].LeaseExpiresAtUtc"] = BsonNull.Value
        });
        await Payments(tenantId).UpdateOneAsync(filter, update, EventArrayOptions(eventId), cancellationToken);
    }

    public Task<List<PaymentDetail>> GetStaleInitiationsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.In(x => x.PaymentStatus, [PaymentStatuses.Initiating, PaymentStatuses.InitiationUnknown]),
            Builders<PaymentDetail>.Filter.Or(
                Builders<PaymentDetail>.Filter.Eq(x => x.ProcessingLeaseExpiresAtUtc, null),
                Builders<PaymentDetail>.Filter.Lte(x => x.ProcessingLeaseExpiresAtUtc, utcNow)));
        return Payments(tenantId).Find(filter).SortBy(x => x.LastUpdatedDateUtc).Limit(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken);
    }

    private IMongoCollection<PaymentDetail> Payments(string tenantId) =>
        _dbContextProvider.GetDatabase(RequireTenant(tenantId)).GetCollection<PaymentDetail>("PaymentDetails");
    private IMongoCollection<PaymentProvider> Providers(string tenantId) =>
        _dbContextProvider.GetDatabase(RequireTenant(tenantId)).GetCollection<PaymentProvider>("PaymentProviders");
    private static UpdateOptions EventArrayOptions(string eventId) => new()
    {
        ArrayFilters = [new BsonDocumentArrayFilterDefinition<PaymentOutboxEvent>(new BsonDocument("evt.EventId", eventId))]
    };
    private static string RequireTenant(string tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId) ? tenantId : throw new InvalidOperationException("A tenant id is required for payment persistence.");
    private static string Sanitize(string value) => value.Length <= 500 ? value : value[..500];
}
