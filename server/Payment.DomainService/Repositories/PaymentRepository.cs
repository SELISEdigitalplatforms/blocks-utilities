using System.Collections.Concurrent;
using Blocks.Genesis;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Repositories;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public PaymentRepository(
        IDbContextProvider dbContextProvider,
        IOptionsMonitor<PaymentOptions> options)
    {
        _dbContextProvider = dbContextProvider;
        _options = options;
    }

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
        var providers = Providers(tenantId);
        // Before creating the organization-scoped index: the one it replaces is narrower, so
        // leaving it in place would keep rejecting a second organization's configuration.
        await DropLegacyProviderIndexAsync(providers, cancellationToken);
        await providers.Indexes.CreateManyAsync(
            PaymentIndexDefinitions.CreateProviderIndexes(),
            cancellationToken);
        _indexedTenants.TryAdd(tenantId, 0);
    }

    private static async Task DropLegacyProviderIndexAsync(
        IMongoCollection<PaymentProvider> providers,
        CancellationToken cancellationToken)
    {
        try
        {
            await providers.Indexes.DropOneAsync(
                PaymentIndexDefinitions.LegacyProviderMerchantIndexName,
                cancellationToken);
        }
        catch (MongoCommandException exception)
            when (exception.Code == 27 ||
                  string.Equals(
                      exception.CodeName,
                      "IndexNotFound",
                      StringComparison.OrdinalIgnoreCase))
        {
            // Never created, or already replaced for this tenant.
        }
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

    public async Task<PaymentProvider?> GetProviderAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        CancellationToken cancellationToken)
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
        var providers = Providers(tenantId);

        // Most specific first: the caller's own configuration, then the tenant's, then the
        // console's — see PaymentProviderScopeChain for why that last one counts as the
        // tenant's. The tenant is already fixed by the caller's token, so this widens which
        // configuration answers, never whose data is reachable.
        foreach (var candidate in PaymentProviderScopeChain.Candidates(
                     organizationId,
                     _options.CurrentValue))
        {
            var found = await providers
                .Find(Builders<PaymentProvider>.Filter.And(
                    filter,
                    Builders<PaymentProvider>.Filter.Eq(
                        x => x.OrganizationId,
                        candidate)))
                .FirstOrDefaultAsync(cancellationToken);

            if (found != null)
            {
                // Returned as stored. Everything downstream that derives an encryption scope
                // reads this row's own organization rather than the one asked for, so a
                // credential stays on the key ring it was sealed under and nothing has to be
                // re-encrypted for a configuration to be shared.
                return found;
            }
        }

        return null;
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

    public Task<PaymentDetail?> GetByPspReferenceAsync(
        string tenantId,
        string pspReference,
        CancellationToken cancellationToken) =>
        Payments(tenantId)
            .Find(payment =>
                payment.TenantId == tenantId &&
                payment.PspReference == pspReference)
            .FirstOrDefaultAsync(cancellationToken)!;

    public Task<PaymentDetail?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken) =>
        Payments(tenantId).Find(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey).FirstOrDefaultAsync(cancellationToken)!;

    public Task<PaymentDetail?>
        GetRecurringPaymentByOrderIdAsync(
            string tenantId,
            string orderId,
            CancellationToken cancellationToken) =>
        Payments(tenantId)
            .Find(payment =>
                payment.TenantId == tenantId &&
                payment.PaymentFlow ==
                PaymentFlows.RecurringCharge &&
                payment.OrderId == orderId)
            .FirstOrDefaultAsync(cancellationToken)!;

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
        Payment.DomainService.Models.ProviderInitiationRequest request,
        string frontendResultUrlSnapshot,
        string returnStateNonceHash,
        string shopperReference,
        CancellationToken cancellationToken,
        string? resolvedProviderId = null,
        string? resolvedProviderOrganizationId = null)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.ProcessingLeaseId, leaseId),
            Builders<PaymentDetail>.Filter.Eq(x => x.PaymentStatus, PaymentStatuses.Initiating));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.InitiationRequest, request)
            .Set(x => x.ProviderReference, request.Reference)
            .Set(x => x.ProviderMerchantAccount, request.MerchantAccount)
            .Set(x => x.FrontendResultUrlSnapshot, frontendResultUrlSnapshot)
            .Set(x => x.ReturnStateNonceHash, returnStateNonceHash)
            .Set(x => x.ShopperReference, shopperReference)
            .Set(x => x.SiteId, request.SiteId)
            .Set(x => x.CaptureMode, request.CaptureMode)
            .Set(x => x.CaptureDelayHours, request.CaptureDelayHours)
            // Persisted here, atomically with the rest of the initiation record and before the
            // provider is ever contacted -- see PaymentProvider resolution in
            // HostedCheckoutInitiationService/PaymentMethodSetupService. Recorded even when null,
            // which correctly means "no expected provider was frozen" rather than being left
            // stale from an unrelated earlier write.
            .Set(x => x.ResolvedProviderId, resolvedProviderId)
            .Set(x => x.ResolvedProviderOrganizationId, resolvedProviderOrganizationId)
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

    public async Task<bool>
        CompleteStoredPaymentChargeInitiationAsync(
            string tenantId,
            string paymentId,
            string leaseId,
            string pspReference,
            string? providerResultCode,
            PaymentOutboxEvent outboxEvent,
            CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.ItemId,
                paymentId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.TenantId,
                tenantId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.ProcessingLeaseId,
                leaseId),
            Builders<PaymentDetail>.Filter.In(
                payment => payment.PaymentStatus,
                [
                    PaymentStatuses.Initiating,
                    PaymentStatuses.InitiationUnknown
                ]),
            Builders<PaymentDetail>.Filter.Not(
                Builders<PaymentDetail>.Filter.ElemMatch(
                    payment => payment.OutboxEvents,
                    item => item.DeduplicationKey ==
                            outboxEvent.DeduplicationKey)));
        var update = Builders<PaymentDetail>.Update
            .Set(
                payment => payment.PaymentStatus,
                PaymentStatuses.Processing)
            .Set(
                payment => payment.PspReference,
                pspReference)
            .Set(
                payment => payment.CheckoutResultCode,
                providerResultCode)
            .Set(
                payment => payment.FailureCode,
                null)
            .Set(
                payment => payment.ProcessingLeaseId,
                null)
            .Set(
                payment => payment.ProcessingLeaseExpiresAtUtc,
                null)
            .Set(
                payment => payment.LastUpdatedDateUtc,
                DateTime.UtcNow)
            .Push(
                payment => payment.OutboxEvents,
                outboxEvent);

        var result = await Payments(tenantId).UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> SaveProviderRoutingAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string providerReference,
        string merchantAccount,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.ItemId,
                paymentId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.TenantId,
                tenantId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.ProcessingLeaseId,
                leaseId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.PaymentStatus,
                PaymentStatuses.Initiating));
        var update = Builders<PaymentDetail>.Update
            .Set(
                payment => payment.ProviderReference,
                providerReference)
            .Set(
                payment => payment.ProviderMerchantAccount,
                merchantAccount)
            .Set(
                payment => payment.LastUpdatedDateUtc,
                DateTime.UtcNow);
        var result = await Payments(tenantId).UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
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
        decimal authorizedAmount,
        bool capturedAutomatically,
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
            .Set(
                x => x.PaymentStatus,
                authorized
                    ? capturedAutomatically
                        ? PaymentStatuses.Captured
                        : PaymentStatuses.Authorized
                    : PaymentStatuses.Refused)
            .Set(
                x => x.AuthorizedAmount,
                authorized ? authorizedAmount : 0)
            .Set(
                x => x.CapturedAmount,
                authorized && capturedAutomatically
                    ? authorizedAmount
                    : 0)
            .Set(
                x => x.CaptureStatus,
                authorized && capturedAutomatically
                    ? PaymentCaptureStatuses.Succeeded
                    : PaymentCaptureStatuses.NotRequested)
            .Set(x => x.PspReference, pspReference)
            .Set(x => x.WebhookConfirmedAtUtc, eventDateUtc)
            .Set(x => x.PaymentInstrument, instrument)
            .Set(x => x.LastUpdatedDateUtc, DateTime.UtcNow)
            .Push(x => x.OutboxEvents, outboxEvent);
        var result = await Payments(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryRecordSetupAuthorizationConfirmedAsync(
        string tenantId,
        string paymentId,
        DateTime eventDateUtc,
        string pspReference,
        CancellationToken cancellationToken)
    {
        // First write wins: the filter only matches while the field is still unset, so a
        // duplicate delivery -- or a genuine race with the token signal's own write -- modifies
        // nothing the second time rather than clobbering the timestamp the first delivery
        // recorded. PspReference is set alongside it even though this write alone never flips
        // PaymentStatus, so a completion triggered later by the token signal still has it.
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.SetupAuthorizationConfirmedAtUtc, null));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.SetupAuthorizationConfirmedAtUtc, eventDateUtc)
            .Set(x => x.PspReference, pspReference)
            .Set(x => x.LastUpdatedDateUtc, DateTime.UtcNow);
        var result = await Payments(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryRecordSetupTokenConfirmedAsync(
        string tenantId,
        string paymentId,
        DateTime eventDateUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.SetupTokenConfirmedAtUtc, null));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.SetupTokenConfirmedAtUtc, eventDateUtc)
            .Set(x => x.LastUpdatedDateUtc, DateTime.UtcNow);
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

    public Task<bool> HasUnresolvedRecurringPaymentAsync(
        string tenantId,
        string storedPaymentMethodId,
        CancellationToken cancellationToken) =>
        Payments(tenantId)
            .Find(payment =>
                payment.TenantId == tenantId &&
                payment.PaymentFlow ==
                PaymentFlows.RecurringCharge &&
                payment.StoredPaymentMethodPublicId ==
                storedPaymentMethodId &&
                (payment.PaymentStatus ==
                     PaymentStatuses.Initiating ||
                 payment.PaymentStatus ==
                     PaymentStatuses.InitiationUnknown ||
                 payment.PaymentStatus ==
                     PaymentStatuses.Processing))
            .Limit(1)
            .AnyAsync(cancellationToken);

    public Task<List<PaymentDetail>> GetDueSetupExpiryCandidatesAsync(
        string tenantId,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.PaymentFlow, PaymentFlows.PaymentMethodSetup),
            Builders<PaymentDetail>.Filter.Eq(x => x.PaymentStatus, PaymentStatuses.Processing),
            Builders<PaymentDetail>.Filter.Lte(x => x.CreatedAtUtc, olderThanUtc),
            Builders<PaymentDetail>.Filter.Or(
                Builders<PaymentDetail>.Filter.Eq(x => x.SetupAuthorizationConfirmedAtUtc, null),
                Builders<PaymentDetail>.Filter.Eq(x => x.SetupTokenConfirmedAtUtc, null)));

        return Payments(tenantId)
            .Find(filter)
            .SortBy(x => x.CreatedAtUtc)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryExpireSetupAsync(
        string tenantId,
        string paymentId,
        DateTime eventDateUtc,
        CancellationToken cancellationToken)
    {
        // Compare-and-set on the status still being Processing AND a signal still being missing,
        // both re-checked atomically as part of this same write -- not just at candidate
        // selection. A completion or an authoritative decline that lands concurrently with the
        // expiry sweep must win over the sweep, never the other way around, and checking
        // Status == Processing alone does not guarantee that: the token or authorization webhook
        // can record its signal (see TryRecordSetupTokenConfirmedAsync /
        // TryRecordSetupAuthorizationConfirmedAsync above) after this call read its candidate list
        // but before this update runs, while the completion that signal unlocks is still in
        // flight or has not yet been retried. Re-verifying "still missing a signal" here, in the
        // same filter that flips the status, is what actually enforces "completion wins" rather
        // than just documenting the intent -- see PR #393 review (Finding 1).
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.ItemId, paymentId),
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.PaymentStatus, PaymentStatuses.Processing),
            Builders<PaymentDetail>.Filter.Or(
                Builders<PaymentDetail>.Filter.Eq(x => x.SetupAuthorizationConfirmedAtUtc, null),
                Builders<PaymentDetail>.Filter.Eq(x => x.SetupTokenConfirmedAtUtc, null)));
        var update = Builders<PaymentDetail>.Update
            .Set(x => x.PaymentStatus, PaymentStatuses.Expired)
            .Set(x => x.LastUpdatedDateUtc, eventDateUtc);
        var result = await Payments(tenantId).UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public Task<List<PaymentDetail>> GetSetupsReadyForCompletionAsync(
        string tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        // Deliberately independent of GetDueSetupExpiryCandidatesAsync's "oldest N regardless of
        // readiness" query -- see this method's own remarks on IPaymentRepository. Filtered to
        // genuinely ready-to-complete records only, so a setup with both signals already on
        // record can never be starved behind an unrelated backlog of older, still-incomplete
        // setups the way sharing one capped query used to allow.
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.PaymentFlow, PaymentFlows.PaymentMethodSetup),
            Builders<PaymentDetail>.Filter.Eq(x => x.PaymentStatus, PaymentStatuses.Processing),
            Builders<PaymentDetail>.Filter.Ne(x => x.SetupAuthorizationConfirmedAtUtc, null),
            Builders<PaymentDetail>.Filter.Ne(x => x.SetupTokenConfirmedAtUtc, null));

        return Payments(tenantId)
            .Find(filter)
            .SortBy(x => x.CreatedAtUtc)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PendingSetupAgeSummary>> GetPendingSetupAgeSummaryAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        // Unlike GetSetupsReadyForCompletionAsync above, this is deliberately uncapped: the whole
        // point is to answer "how old is the oldest offender in each missing-signal category"
        // across every pending setup for the tenant, computed by Mongo's own aggregation rather
        // than paging documents into application code to inspect one field on each.
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(x => x.PaymentFlow, PaymentFlows.PaymentMethodSetup),
            Builders<PaymentDetail>.Filter.Eq(x => x.PaymentStatus, PaymentStatuses.Processing),
            Builders<PaymentDetail>.Filter.Or(
                Builders<PaymentDetail>.Filter.Eq(x => x.SetupAuthorizationConfirmedAtUtc, null),
                Builders<PaymentDetail>.Filter.Eq(x => x.SetupTokenConfirmedAtUtc, null)));

        var grouped = await Payments(tenantId)
            .Aggregate()
            .Match(filter)
            .Group(
                payment => new
                {
                    MissingAuthorization = payment.SetupAuthorizationConfirmedAtUtc == null,
                    MissingToken = payment.SetupTokenConfirmedAtUtc == null
                },
                group => new
                {
                    group.Key,
                    Count = group.LongCount(),
                    OldestCreatedAtUtc = group.Min(payment => payment.CreatedAtUtc)
                })
            .ToListAsync(cancellationToken);

        return grouped
            .Select(entry => new PendingSetupAgeSummary(
                MissingSignalOf(entry.Key.MissingAuthorization, entry.Key.MissingToken),
                entry.Count,
                entry.OldestCreatedAtUtc))
            .ToList();
    }

    private static string MissingSignalOf(bool missingAuthorization, bool missingToken) =>
        (missingAuthorization, missingToken) switch
        {
            (true, true) => "both",
            (true, false) => "authorization",
            (false, true) => "token",
            (false, false) => "none"
        };

    public async Task<bool> TryCreateProviderAsync(
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        await EnsureIndexesAsync(provider.TenantId!, cancellationToken);

        try
        {
            await Providers(provider.TenantId!)
                .InsertOneAsync(provider, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The unique index is the arbiter, so two concurrent registrations for the same
            // merchant cannot both land.
            return false;
        }
    }

    public async Task<IReadOnlyList<PaymentProvider>> GetProvidersAsync(
        string tenantId,
        CancellationToken cancellationToken) =>
        await Providers(tenantId)
            .Find(Builders<PaymentProvider>.Filter.Eq(x => x.TenantId, tenantId))
            .ToListAsync(cancellationToken);

    public async Task<PaymentProvider?> GetProviderByIdAsync(
        string tenantId,
        string providerItemId,
        CancellationToken cancellationToken) =>
        await Providers(tenantId)
            .Find(Builders<PaymentProvider>.Filter.And(
                Builders<PaymentProvider>.Filter.Eq(
                    provider => provider.ItemId,
                    providerItemId),
                Builders<PaymentProvider>.Filter.Eq(
                    provider => provider.TenantId,
                    tenantId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PaymentProvider?> TryUpdateProviderConfigurationAsync(
        string tenantId,
        string providerItemId,
        long expectedVersion,
        string frontendResultUrl,
        string? countryCode,
        bool manualCapture,
        int maxRefundDays,
        string? storeId,
        bool isEnabled,
        string? paymentMethodConfigurationId,
        string[]? checkoutPaymentMethodTypes,
        CancellationToken cancellationToken)
    {
        var filter = ProviderVersionFilter(
            tenantId,
            providerItemId,
            expectedVersion);
        var update = Builders<PaymentProvider>.Update
            .Set(
                provider => provider.FrontendResultUrl,
                frontendResultUrl)
            .Set(provider => provider.CountryCode, countryCode)
            .Set(provider => provider.ManualCapture, manualCapture)
            .Set(provider => provider.MaxRefundDays, maxRefundDays)
            .Set(provider => provider.StoreId, storeId)
            .Set(provider => provider.IsEnabled, isEnabled)
            .Set(
                provider => provider.PaymentMethodConfigurationId,
                paymentMethodConfigurationId)
            .Set(
                provider => provider.CheckoutPaymentMethodTypes,
                checkoutPaymentMethodTypes)
            .Inc(provider => provider.Version, 1);

        return await Providers(tenantId).FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<
                PaymentProvider,
                PaymentProvider>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    public async Task<PaymentProvider?> TryRotateProviderCredentialsAsync(
        string tenantId,
        string providerItemId,
        long expectedVersion,
        string providerSecretsCiphertext,
        string tenantSecuritySecretsCiphertext,
        string encryptionKeyId,
        CancellationToken cancellationToken)
    {
        var filter = ProviderVersionFilter(
            tenantId,
            providerItemId,
            expectedVersion);
        var update = Builders<PaymentProvider>.Update
            .Set(
                provider => provider.ProviderSecretsCiphertext,
                providerSecretsCiphertext)
            .Set(
                provider => provider.TenantSecuritySecretsCiphertext,
                tenantSecuritySecretsCiphertext)
            .Set(
                provider => provider.SecretsEncryptionKeyId,
                encryptionKeyId)
            .Inc(provider => provider.Version, 1);

        return await Providers(tenantId).FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<
                PaymentProvider,
                PaymentProvider>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    public async Task<bool> SaveProviderSecretsAsync(
        string tenantId,
        string providerItemId,
        string providerSecretsCiphertext,
        string tenantSecuritySecretsCiphertext,
        string encryptionKeyId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentProvider>.Filter.And(
            Builders<PaymentProvider>.Filter.Eq(x => x.ItemId, providerItemId),
            Builders<PaymentProvider>.Filter.Eq(x => x.TenantId, tenantId),
            // Compare-and-set on absence: a provider that already holds credentials is never
            // rewritten, so a repeated migration is safe.
            Builders<PaymentProvider>.Filter.Or(
                Builders<PaymentProvider>.Filter.Exists(x => x.ProviderSecretsCiphertext, false),
                Builders<PaymentProvider>.Filter.Eq(x => x.ProviderSecretsCiphertext, null)));
        var update = Builders<PaymentProvider>.Update
            .Set(x => x.ProviderSecretsCiphertext, providerSecretsCiphertext)
            .Set(x => x.TenantSecuritySecretsCiphertext, tenantSecuritySecretsCiphertext)
            .Set(x => x.SecretsEncryptionKeyId, encryptionKeyId);

        var result = await Providers(tenantId)
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> ReplaceProviderSecretsAsync(
        string tenantId,
        string providerItemId,
        string expectedKeyId,
        string providerSecretsCiphertext,
        string tenantSecuritySecretsCiphertext,
        string encryptionKeyId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentProvider>.Filter.And(
            Builders<PaymentProvider>.Filter.Eq(x => x.ItemId, providerItemId),
            Builders<PaymentProvider>.Filter.Eq(x => x.TenantId, tenantId),
            // Compare-and-set on the key that produced the ciphertext we decrypted. A provider
            // already moved on — by a concurrent rotation, or a previous run — is skipped.
            Builders<PaymentProvider>.Filter.Eq(
                x => x.SecretsEncryptionKeyId,
                expectedKeyId));
        var update = Builders<PaymentProvider>.Update
            .Set(x => x.ProviderSecretsCiphertext, providerSecretsCiphertext)
            .Set(x => x.TenantSecuritySecretsCiphertext, tenantSecuritySecretsCiphertext)
            .Set(x => x.SecretsEncryptionKeyId, encryptionKeyId);

        var result = await Providers(tenantId)
            .UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    private IMongoCollection<PaymentDetail> Payments(string tenantId) =>
        _dbContextProvider.GetDatabase(RequireTenant(tenantId)).GetCollection<PaymentDetail>("PaymentDetails");
    private IMongoCollection<PaymentProvider> Providers(string tenantId) =>
        _dbContextProvider.GetDatabase(RequireTenant(tenantId)).GetCollection<PaymentProvider>("PaymentProviders");

    private static FilterDefinition<PaymentProvider> ProviderVersionFilter(
        string tenantId,
        string providerItemId,
        long expectedVersion)
    {
        var versionFilter = expectedVersion == 0
            ? Builders<PaymentProvider>.Filter.Or(
                Builders<PaymentProvider>.Filter.Eq(
                    provider => provider.Version,
                    0),
                Builders<PaymentProvider>.Filter.Exists(
                    provider => provider.Version,
                    false))
            : Builders<PaymentProvider>.Filter.Eq(
                provider => provider.Version,
                expectedVersion);

        return Builders<PaymentProvider>.Filter.And(
            Builders<PaymentProvider>.Filter.Eq(
                provider => provider.ItemId,
                providerItemId),
            Builders<PaymentProvider>.Filter.Eq(
                provider => provider.TenantId,
                tenantId),
            versionFilter);
    }

    private static UpdateOptions EventArrayOptions(string eventId) => new()
    {
        ArrayFilters = [new BsonDocumentArrayFilterDefinition<PaymentOutboxEvent>(new BsonDocument("evt.EventId", eventId))]
    };
    private static string RequireTenant(string tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId) ? tenantId : throw new InvalidOperationException("A tenant id is required for payment persistence.");
    private static string Sanitize(string value) => value.Length <= 500 ? value : value[..500];
}
