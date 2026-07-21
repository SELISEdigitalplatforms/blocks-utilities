using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public sealed class PaymentCaptureRepository :
    IPaymentCaptureRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte>
        _indexedTenants = new();

    public PaymentCaptureRepository(
        IDbContextProvider dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Payments(tenantId).Indexes.CreateManyAsync(
            [
                new CreateIndexModel<PaymentDetail>(
                    Builders<PaymentDetail>.IndexKeys
                        .Ascending(payment => payment.TenantId)
                        .Ascending("Captures.IdempotencyKey"),
                    new CreateIndexOptions<PaymentDetail>
                    {
                        Unique = true,
                        Name = "ux_payment_capture_tenant_idempotency",
                        PartialFilterExpression = new BsonDocument(
                            "Captures.IdempotencyKey",
                            new BsonDocument("$type", "string"))
                    }),
                new CreateIndexModel<PaymentDetail>(
                    Builders<PaymentDetail>.IndexKeys
                        .Ascending(payment => payment.TenantId)
                        .Ascending("Captures.CaptureId"),
                    new CreateIndexOptions<PaymentDetail>
                    {
                        Unique = true,
                        Name = "ux_payment_capture_tenant_id",
                        PartialFilterExpression = new BsonDocument(
                            "Captures.CaptureId",
                            new BsonDocument("$type", "string"))
                    })
            ],
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public Task<PaymentDetail?> GetPaymentAsync(
        string tenantId,
        string paymentDetailId,
        CancellationToken cancellationToken) =>
        Payments(tenantId)
            .Find(payment =>
                payment.TenantId == tenantId &&
                payment.ItemId == paymentDetailId)
            .FirstOrDefaultAsync(cancellationToken)!;

    public Task<PaymentDetail?> GetPaymentByCaptureIdAsync(
        string tenantId,
        string captureId,
        CancellationToken cancellationToken) =>
        Payments(tenantId)
            .Find(Builders<PaymentDetail>.Filter.And(
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.TenantId,
                    tenantId),
                Builders<PaymentDetail>.Filter.ElemMatch(
                    payment => payment.Captures,
                    capture => capture.CaptureId == captureId)))
            .FirstOrDefaultAsync(cancellationToken)!;

    public Task<PaymentDetail?> GetPaymentByIdempotencyKeyAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        Payments(tenantId)
            .Find(Builders<PaymentDetail>.Filter.And(
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.TenantId,
                    tenantId),
                Builders<PaymentDetail>.Filter.ElemMatch(
                    payment => payment.Captures,
                    capture =>
                        capture.IdempotencyKey == idempotencyKey)))
            .FirstOrDefaultAsync(cancellationToken)!;

    public async Task<bool> TryReserveAsync(
        string tenantId,
        string paymentDetailId,
        PaymentCapture capture,
        int maximumCaptures,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var availableAmount = new BsonDocument(
            "$subtract",
            new BsonArray
            {
                new BsonDocument(
                    "$subtract",
                    new BsonArray
                    {
                        "$AuthorizedAmount",
                        "$CapturedAmount"
                    }),
                "$ReservedCaptureAmount"
            });
        var captureCount = new BsonDocument(
            "$size",
            new BsonDocument(
                "$ifNull",
                new BsonArray { "$Captures", new BsonArray() }));
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.ItemId,
                paymentDetailId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.TenantId,
                tenantId),
            Builders<PaymentDetail>.Filter.In(
                payment => payment.PaymentStatus,
                [
                    PaymentStatuses.Authorized,
                    PaymentStatuses.PartiallyCaptured
                ]),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.CurrencyCode,
                capture.CurrencyCode),
            new BsonDocument(
                "$expr",
                new BsonDocument(
                    "$and",
                    new BsonArray
                    {
                        new BsonDocument(
                            "$gte",
                            new BsonArray
                            {
                                availableAmount,
                                new Decimal128(capture.Amount)
                            }),
                        new BsonDocument(
                            "$lt",
                            new BsonArray
                            {
                                captureCount,
                                maximumCaptures
                            })
                    })));
        var update = Builders<PaymentDetail>.Update
            .Push(payment => payment.Captures, capture)
            .Inc(
                payment => payment.ReservedCaptureAmount,
                capture.Amount)
            .Set(
                payment => payment.CaptureStatus,
                PaymentCaptureStatuses.Initiating)
            .Set(
                payment => payment.LastUpdatedDateUtc,
                DateTime.UtcNow);

        try
        {
            var result = await Payments(tenantId).UpdateOneAsync(
                filter,
                update,
                cancellationToken: cancellationToken);

            return result.ModifiedCount == 1;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category ==
                  ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<PaymentCapture?> TryClaimInitiationAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = new BsonDocument
        {
            ["_id"] = paymentDetailId,
            ["TenantId"] = tenantId,
            ["Captures"] = new BsonDocument(
                "$elemMatch",
                new BsonDocument
                {
                    ["CaptureId"] = captureId,
                    ["Status"] = new BsonDocument(
                        "$in",
                        new BsonArray
                        {
                            PaymentCaptureStatuses.Initiating,
                            PaymentCaptureStatuses.InitiationUnknown
                        }),
                    ["$or"] = new BsonArray
                    {
                        new BsonDocument(
                            "ProcessingLeaseExpiresAtUtc",
                            BsonNull.Value),
                        new BsonDocument(
                            "ProcessingLeaseExpiresAtUtc",
                            new BsonDocument("$lte", now))
                    }
                })
        };
        var update = new BsonDocument
        {
            ["$set"] = new BsonDocument
            {
                ["Captures.$[capture].Status"] =
                    PaymentCaptureStatuses.Initiating,
                ["Captures.$[capture].ProcessingLeaseId"] = leaseId,
                ["Captures.$[capture].ProcessingLeaseExpiresAtUtc"] =
                    leaseExpiresAtUtc,
                ["Captures.$[capture].UpdatedAtUtc"] = now
            },
            ["$inc"] = new BsonDocument(
                "Captures.$[capture].InitiationAttemptCount",
                1)
        };
        var result = await Payments(tenantId).FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<PaymentDetail, PaymentDetail>
            {
                ArrayFilters =
                [
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(
                        new BsonDocument(
                            "capture.CaptureId",
                            captureId))
                ],
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        return result?.Captures.FirstOrDefault(
            capture => capture.CaptureId == captureId);
    }

    public Task<bool> CompleteSubmissionAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string leaseId,
        string providerCaptureReference,
        string? providerStatus,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken) =>
        UpdateAndReturnAsync(
            tenantId,
            CaptureLeaseFilter(
                tenantId,
                paymentDetailId,
                captureId,
                leaseId),
            new BsonDocument
            {
                ["$set"] = new BsonDocument
                {
                    ["Captures.$[capture].Status"] =
                        PaymentCaptureStatuses.Submitted,
                    ["Captures.$[capture].ProviderCaptureReference"] =
                        providerCaptureReference,
                    ["Captures.$[capture].ProviderResultStatus"] =
                        providerStatus == null
                            ? BsonNull.Value
                            : providerStatus,
                    ["Captures.$[capture].SubmittedAtUtc"] =
                        DateTime.UtcNow,
                    ["Captures.$[capture].UpdatedAtUtc"] =
                        DateTime.UtcNow,
                    ["Captures.$[capture].ProcessingLeaseId"] =
                        BsonNull.Value,
                    ["Captures.$[capture].ProcessingLeaseExpiresAtUtc"] =
                        BsonNull.Value,
                    ["CaptureStatus"] =
                        PaymentCaptureStatuses.Submitted,
                    ["LastUpdatedDateUtc"] = DateTime.UtcNow
                },
                ["$push"] = new BsonDocument(
                    "OutboxEvents",
                    outboxEvent.ToBsonDocument())
            },
            captureId,
            cancellationToken);

    public Task<bool> CompleteRejectionAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string leaseId,
        decimal amount,
        string failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken) =>
        UpdateAndReturnAsync(
            tenantId,
            CaptureLeaseFilter(
                tenantId,
                paymentDetailId,
                captureId,
                leaseId),
            new BsonDocument
            {
                ["$set"] = new BsonDocument
                {
                    ["Captures.$[capture].Status"] =
                        PaymentCaptureStatuses.Failed,
                    ["Captures.$[capture].FailureCode"] = failureCode,
                    ["Captures.$[capture].CompletedAtUtc"] =
                        DateTime.UtcNow,
                    ["Captures.$[capture].UpdatedAtUtc"] =
                        DateTime.UtcNow,
                    ["Captures.$[capture].ProcessingLeaseId"] =
                        BsonNull.Value,
                    ["Captures.$[capture].ProcessingLeaseExpiresAtUtc"] =
                        BsonNull.Value,
                    ["CaptureStatus"] = PaymentCaptureStatuses.Failed,
                    ["LastUpdatedDateUtc"] = DateTime.UtcNow
                },
                ["$inc"] = new BsonDocument(
                    "ReservedCaptureAmount",
                    new Decimal128(-amount)),
                ["$push"] = new BsonDocument(
                    "OutboxEvents",
                    outboxEvent.ToBsonDocument())
            },
            captureId,
            cancellationToken);

    public async Task MarkInitiationUnknownAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string leaseId,
        string failureCode,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        var update = new BsonDocument(
            "$set",
            new BsonDocument
            {
                ["Captures.$[capture].Status"] =
                    PaymentCaptureStatuses.InitiationUnknown,
                ["Captures.$[capture].FailureCode"] = failureCode,
                ["Captures.$[capture].NextRecoveryAttemptAtUtc"] =
                    nextAttemptAtUtc,
                ["Captures.$[capture].UpdatedAtUtc"] = DateTime.UtcNow,
                ["Captures.$[capture].ProcessingLeaseId"] =
                    BsonNull.Value,
                ["Captures.$[capture].ProcessingLeaseExpiresAtUtc"] =
                    BsonNull.Value,
                ["CaptureStatus"] =
                    PaymentCaptureStatuses.InitiationUnknown,
                ["LastUpdatedDateUtc"] = DateTime.UtcNow
            });

        await Payments(tenantId).UpdateOneAsync(
            CaptureLeaseFilter(
                tenantId,
                paymentDetailId,
                captureId,
                leaseId),
            update,
            CaptureUpdateOptions(captureId),
            cancellationToken);
    }

    public async Task MarkRequiresAttentionAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string? leaseId,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var captureMatch = new BsonDocument
        {
            ["CaptureId"] = captureId,
            ["Status"] = new BsonDocument(
                "$in",
                new BsonArray
                {
                    PaymentCaptureStatuses.Initiating,
                    PaymentCaptureStatuses.InitiationUnknown
                })
        };

        if (leaseId != null)
        {
            captureMatch["ProcessingLeaseId"] = leaseId;
        }

        var filter = new BsonDocument
        {
            ["_id"] = paymentDetailId,
            ["TenantId"] = tenantId,
            ["Captures"] = new BsonDocument(
                "$elemMatch",
                captureMatch)
        };
        var update = new BsonDocument(
            "$set",
            new BsonDocument
            {
                ["Captures.$[capture].Status"] =
                    PaymentCaptureStatuses.RequiresAttention,
                ["Captures.$[capture].FailureCode"] = failureCode,
                ["Captures.$[capture].UpdatedAtUtc"] = DateTime.UtcNow,
                ["Captures.$[capture].ProcessingLeaseId"] =
                    BsonNull.Value,
                ["Captures.$[capture].ProcessingLeaseExpiresAtUtc"] =
                    BsonNull.Value,
                ["CaptureStatus"] =
                    PaymentCaptureStatuses.RequiresAttention,
                ["LastUpdatedDateUtc"] = DateTime.UtcNow
            });

        await Payments(tenantId).UpdateOneAsync(
            filter,
            update,
            CaptureUpdateOptions(captureId),
            cancellationToken);
    }

    public Task<List<PaymentDetail>>
        GetPaymentsWithDueCaptureInitiationsAsync(
            string tenantId,
            DateTime utcNow,
            int limit,
            CancellationToken cancellationToken)
    {
        var captureFilter = Builders<PaymentCapture>.Filter.And(
            Builders<PaymentCapture>.Filter.In(
                capture => capture.Status,
                [
                    PaymentCaptureStatuses.Initiating,
                    PaymentCaptureStatuses.InitiationUnknown
                ]),
            Builders<PaymentCapture>.Filter.Lte(
                capture => capture.NextRecoveryAttemptAtUtc,
                utcNow),
            Builders<PaymentCapture>.Filter.Or(
                Builders<PaymentCapture>.Filter.Eq(
                    capture => capture.ProcessingLeaseExpiresAtUtc,
                    null),
                Builders<PaymentCapture>.Filter.Lte(
                    capture => capture.ProcessingLeaseExpiresAtUtc,
                    utcNow)));

        return Payments(tenantId)
            .Find(Builders<PaymentDetail>.Filter.And(
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.TenantId,
                    tenantId),
                Builders<PaymentDetail>.Filter.ElemMatch(
                    payment => payment.Captures,
                    captureFilter)))
            .SortBy(payment => payment.LastUpdatedDateUtc)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ApplyProviderEventAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        IReadOnlyCollection<string> expectedStatuses,
        string targetCaptureStatus,
        string targetPaymentStatus,
        string providerCaptureReference,
        DateTime eventDateUtc,
        decimal reservedAmountDelta,
        decimal capturedAmountDelta,
        string? failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        var filter = new BsonDocument
        {
            ["_id"] = paymentDetailId,
            ["TenantId"] = tenantId,
            ["Captures"] = new BsonDocument(
                "$elemMatch",
                new BsonDocument
                {
                    ["CaptureId"] = captureId,
                    ["Status"] = new BsonDocument(
                        "$in",
                        new BsonArray(expectedStatuses)),
                    ["$or"] = new BsonArray
                    {
                        new BsonDocument(
                            "LastProviderEventAtUtc",
                            BsonNull.Value),
                        new BsonDocument(
                            "LastProviderEventAtUtc",
                            new BsonDocument("$lte", eventDateUtc))
                    }
                })
        };
        var update = new BsonDocument
        {
            ["$set"] = new BsonDocument
            {
                ["Captures.$[capture].Status"] = targetCaptureStatus,
                ["Captures.$[capture].ProviderCaptureReference"] =
                    providerCaptureReference,
                ["Captures.$[capture].FailureCode"] =
                    failureCode == null
                        ? BsonNull.Value
                        : failureCode,
                ["Captures.$[capture].LastProviderEventAtUtc"] =
                    eventDateUtc,
                ["Captures.$[capture].CompletedAtUtc"] =
                    eventDateUtc,
                ["Captures.$[capture].UpdatedAtUtc"] =
                    DateTime.UtcNow,
                ["CaptureStatus"] = targetCaptureStatus,
                ["PaymentStatus"] = targetPaymentStatus,
                ["LastCaptureEventAtUtc"] = eventDateUtc,
                ["LastUpdatedDateUtc"] = DateTime.UtcNow
            },
            ["$inc"] = new BsonDocument
            {
                ["ReservedCaptureAmount"] =
                    new Decimal128(reservedAmountDelta),
                ["CapturedAmount"] =
                    new Decimal128(capturedAmountDelta)
            },
            ["$push"] = new BsonDocument(
                "OutboxEvents",
                outboxEvent.ToBsonDocument())
        };

        return UpdateAndReturnAsync(
            tenantId,
            filter,
            update,
            captureId,
            cancellationToken);
    }

    private async Task<bool> UpdateAndReturnAsync(
        string tenantId,
        FilterDefinition<PaymentDetail> filter,
        UpdateDefinition<PaymentDetail> update,
        string captureId,
        CancellationToken cancellationToken)
    {
        var result = await Payments(tenantId).UpdateOneAsync(
            filter,
            update,
            CaptureUpdateOptions(captureId),
            cancellationToken);

        return result.ModifiedCount == 1;
    }

    private IMongoCollection<PaymentDetail> Payments(
        string tenantId) =>
        _dbContextProvider.GetDatabase(RequireTenant(tenantId))
            .GetCollection<PaymentDetail>("PaymentDetails");

    private static string RequireTenant(string tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId)
            ? tenantId
            : throw new InvalidOperationException(
                "A tenant id is required.");

    private static FilterDefinition<PaymentDetail>
        CaptureLeaseFilter(
            string tenantId,
            string paymentDetailId,
            string captureId,
            string leaseId) =>
        new BsonDocument
        {
            ["_id"] = paymentDetailId,
            ["TenantId"] = tenantId,
            ["Captures"] = new BsonDocument(
                "$elemMatch",
                new BsonDocument
                {
                    ["CaptureId"] = captureId,
                    ["ProcessingLeaseId"] = leaseId,
                    ["Status"] = PaymentCaptureStatuses.Initiating
                })
        };

    private static UpdateOptions CaptureUpdateOptions(
        string captureId) =>
        new()
        {
            ArrayFilters =
            [
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument(
                        "capture.CaptureId",
                        captureId))
            ]
        };
}
