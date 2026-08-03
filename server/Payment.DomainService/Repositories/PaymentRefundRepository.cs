using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public sealed class PaymentRefundRepository :
    IPaymentRefundRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte>
        _indexedTenants = new();

    public PaymentRefundRepository(
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
                        .Ascending(
                            "Refunds.IdempotencyKey"),
                    new CreateIndexOptions<PaymentDetail>
                    {
                        Unique = true,
                        Name =
                            "ux_payment_refund_tenant_idempotency",
                        PartialFilterExpression =
                            new BsonDocument(
                                "Refunds.IdempotencyKey",
                                new BsonDocument(
                                    "$type",
                                    "string"))
                    }),
                new CreateIndexModel<PaymentDetail>(
                    Builders<PaymentDetail>.IndexKeys
                        .Ascending(payment => payment.TenantId)
                        .Ascending("Refunds.RefundId"),
                    new CreateIndexOptions<PaymentDetail>
                    {
                        Unique = true,
                        Name =
                            "ux_payment_refund_tenant_id",
                        PartialFilterExpression =
                            new BsonDocument(
                                "Refunds.RefundId",
                                new BsonDocument(
                                    "$type",
                                    "string"))
                    }),
                new CreateIndexModel<PaymentDetail>(
                    Builders<PaymentDetail>.IndexKeys
                        .Ascending("Refunds.Status")
                        .Ascending(
                            "Refunds.NextRecoveryAttemptAtUtc")
                        .Ascending(
                            "Refunds.ProcessingLeaseExpiresAtUtc"),
                    new CreateIndexOptions
                    {
                        Name =
                            "ix_payment_refund_recovery_due"
                    }),
                new CreateIndexModel<PaymentDetail>(
                    Builders<PaymentDetail>.IndexKeys
                        .Ascending(
                            "Refunds.OutboxEvents.Status")
                        .Ascending(
                            "Refunds.OutboxEvents.NextAttemptAtUtc"),
                    new CreateIndexOptions
                    {
                        Name =
                            "ix_payment_refund_outbox_due"
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

    public Task<PaymentDetail?> GetPaymentByRefundIdAsync(
        string tenantId,
        string refundId,
        CancellationToken cancellationToken) =>
        Payments(tenantId)
            .Find(
                Builders<PaymentDetail>.Filter.And(
                    Builders<PaymentDetail>.Filter.Eq(
                        payment => payment.TenantId,
                        tenantId),
                    Builders<PaymentDetail>.Filter.ElemMatch(
                        payment => payment.Refunds,
                        refund =>
                            refund.RefundId == refundId)))
            .FirstOrDefaultAsync(cancellationToken)!;

    public Task<PaymentDetail?>
        GetPaymentByRefundIdempotencyKeyAsync(
            string tenantId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
        Payments(tenantId)
            .Find(
                Builders<PaymentDetail>.Filter.And(
                    Builders<PaymentDetail>.Filter.Eq(
                        payment => payment.TenantId,
                        tenantId),
                    Builders<PaymentDetail>.Filter.ElemMatch(
                        payment => payment.Refunds,
                        refund =>
                            refund.IdempotencyKey ==
                            idempotencyKey)))
            .FirstOrDefaultAsync(cancellationToken)!;

    public async Task<bool> TryReserveAsync(
        string tenantId,
        string paymentDetailId,
        PaymentRefund refund,
        int maximumRefunds,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(
            tenantId,
            cancellationToken);

        var availableAmount = new BsonDocument(
            "$subtract",
            new BsonArray
            {
                new BsonDocument(
                    "$subtract",
                    new BsonArray
                    {
                        "$CapturedAmount",
                        "$RefundedAmount"
                    }),
                "$ReservedRefundAmount"
            });
        BsonDocument amountIsAvailable =
            refund.ProviderOperation ==
            PaymentFundReturnOperations.Reversal
                ? new BsonDocument(
                    "$and",
                    new BsonArray
                    {
                        new BsonDocument(
                            "$eq",
                            new BsonArray
                            {
                                "$PreciseAmount",
                                new Decimal128(refund.Amount)
                            }),
                        new BsonDocument(
                            "$eq",
                            new BsonArray
                            {
                                "$RefundedAmount",
                                Decimal128.Zero
                            }),
                        new BsonDocument(
                            "$eq",
                            new BsonArray
                            {
                                "$ReservedRefundAmount",
                                Decimal128.Zero
                            })
                    })
                : new BsonDocument(
                    "$gte",
                    new BsonArray
                    {
                        availableAmount,
                        new Decimal128(refund.Amount)
                    });
        var refundCount = new BsonDocument(
            "$size",
            new BsonDocument(
                "$ifNull",
                new BsonArray
                {
                    "$Refunds",
                    new BsonArray()
                }));
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
                    PaymentStatuses.Captured,
                    PaymentStatuses.PartiallyCaptured,
                    PaymentStatuses.PartiallyRefunded
                ]),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.CurrencyCode,
                refund.CurrencyCode),
            new BsonDocument(
                "$expr",
                new BsonDocument(
                    "$and",
                    new BsonArray
                    {
                        amountIsAvailable,
                        new BsonDocument(
                            "$lt",
                            new BsonArray
                            {
                                refundCount,
                                maximumRefunds
                            })
                    })));
        var update = Builders<PaymentDetail>.Update
            .Push(payment => payment.Refunds, refund)
            .Inc(
                payment => payment.ReservedRefundAmount,
                refund.Amount)
            .Set(
                payment => payment.LastUpdatedDateUtc,
                DateTime.UtcNow);

        try
        {
            var result = await Payments(tenantId)
                .UpdateOneAsync(
                    filter,
                    update,
                    cancellationToken:
                    cancellationToken);

            return result.ModifiedCount == 1;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category ==
                  ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<PaymentRefund?> TryClaimInitiationAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = new BsonDocument
        {
            ["_id"] = paymentDetailId,
            ["TenantId"] = tenantId,
            ["Refunds"] = new BsonDocument(
                "$elemMatch",
                new BsonDocument
                {
                    ["RefundId"] = refundId,
                    ["Status"] = new BsonDocument(
                        "$in",
                        new BsonArray
                        {
                            PaymentRefundStatuses.Initiating,
                            PaymentRefundStatuses
                                .InitiationUnknown
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
                ["Refunds.$[refund].Status"] =
                    PaymentRefundStatuses.Initiating,
                ["Refunds.$[refund].ProcessingLeaseId"] =
                    leaseId,
                ["Refunds.$[refund].ProcessingLeaseExpiresAtUtc"] =
                    leaseExpiresAtUtc,
                ["Refunds.$[refund].UpdatedAtUtc"] = now
            },
            ["$inc"] = new BsonDocument(
                "Refunds.$[refund].InitiationAttemptCount",
                1)
        };
        var result = await Payments(tenantId)
            .FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<
                    PaymentDetail,
                    PaymentDetail>
                {
                    ArrayFilters =
                    [
                        RefundArrayFilter(refundId)
                    ],
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);

        return result?.Refunds.FirstOrDefault(
            refund => refund.RefundId == refundId);
    }

    public async Task<bool> CompleteSubmissionAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string leaseId,
        string providerRefundReference,
        string? providerStatus,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = RefundLeaseFilter(
            tenantId,
            paymentDetailId,
            refundId,
            leaseId);
        var update = new BsonDocument
        {
            ["$set"] = new BsonDocument
            {
                ["Refunds.$[refund].Status"] =
                    PaymentRefundStatuses.Submitted,
                ["Refunds.$[refund].ProviderRefundReference"] =
                    providerRefundReference,
                ["Refunds.$[refund].ProviderResultStatus"] =
                    providerStatus == null
                        ? BsonNull.Value
                        : new BsonString(providerStatus),
                ["Refunds.$[refund].SubmittedAtUtc"] = now,
                ["Refunds.$[refund].UpdatedAtUtc"] = now,
                ["Refunds.$[refund].ProcessingLeaseId"] =
                    BsonNull.Value,
                ["Refunds.$[refund].ProcessingLeaseExpiresAtUtc"] =
                    BsonNull.Value,
                ["LastUpdatedDateUtc"] = now
            },
            ["$push"] = new BsonDocument(
                "Refunds.$[refund].OutboxEvents",
                outboxEvent.ToBsonDocument())
        };

        var result = await Payments(tenantId).UpdateOneAsync(
            filter,
            update,
            RefundUpdateOptions(refundId),
            cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> CompleteRejectionAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string leaseId,
        decimal amount,
        string failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = RefundLeaseFilter(
            tenantId,
            paymentDetailId,
            refundId,
            leaseId);
        var update = new BsonDocument
        {
            ["$set"] = new BsonDocument
            {
                ["Refunds.$[refund].Status"] =
                    PaymentRefundStatuses.Failed,
                ["Refunds.$[refund].FailureCode"] =
                    failureCode,
                ["Refunds.$[refund].CompletedAtUtc"] = now,
                ["Refunds.$[refund].UpdatedAtUtc"] = now,
                ["Refunds.$[refund].ProcessingLeaseId"] =
                    BsonNull.Value,
                ["Refunds.$[refund].ProcessingLeaseExpiresAtUtc"] =
                    BsonNull.Value,
                ["LastUpdatedDateUtc"] = now
            },
            ["$inc"] = new BsonDocument(
                "ReservedRefundAmount",
                new Decimal128(-amount)),
            ["$push"] = new BsonDocument(
                "Refunds.$[refund].OutboxEvents",
                outboxEvent.ToBsonDocument())
        };

        var result = await Payments(tenantId).UpdateOneAsync(
            filter,
            update,
            RefundUpdateOptions(refundId),
            cancellationToken);

        return result.ModifiedCount == 1;
    }

    public Task MarkInitiationUnknownAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string leaseId,
        string failureCode,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var update = new BsonDocument(
            "$set",
            new BsonDocument
            {
                ["Refunds.$[refund].Status"] =
                    PaymentRefundStatuses
                        .InitiationUnknown,
                ["Refunds.$[refund].FailureCode"] =
                    failureCode,
                ["Refunds.$[refund].NextRecoveryAttemptAtUtc"] =
                    nextAttemptAtUtc,
                ["Refunds.$[refund].UpdatedAtUtc"] = now,
                ["Refunds.$[refund].ProcessingLeaseId"] =
                    BsonNull.Value,
                ["Refunds.$[refund].ProcessingLeaseExpiresAtUtc"] =
                    BsonNull.Value,
                ["LastUpdatedDateUtc"] = now
            });

        return Payments(tenantId).UpdateOneAsync(
            RefundLeaseFilter(
                tenantId,
                paymentDetailId,
                refundId,
                leaseId),
            update,
            RefundUpdateOptions(refundId),
            cancellationToken);
    }

    public Task MarkRequiresAttentionAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string? leaseId,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.ItemId,
                paymentDetailId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.TenantId,
                tenantId),
            Builders<PaymentDetail>.Filter.ElemMatch(
                payment => payment.Refunds,
                refund =>
                    refund.RefundId == refundId &&
                    (leaseId == null ||
                     refund.ProcessingLeaseId ==
                     leaseId)));
        var update = new BsonDocument(
            "$set",
            new BsonDocument
            {
                ["Refunds.$[refund].Status"] =
                    PaymentRefundStatuses
                        .RequiresAttention,
                ["Refunds.$[refund].FailureCode"] =
                    failureCode,
                ["Refunds.$[refund].UpdatedAtUtc"] =
                    DateTime.UtcNow,
                ["Refunds.$[refund].ProcessingLeaseId"] =
                    BsonNull.Value,
                ["Refunds.$[refund].ProcessingLeaseExpiresAtUtc"] =
                    BsonNull.Value,
                ["LastUpdatedDateUtc"] =
                    DateTime.UtcNow
            });

        return Payments(tenantId).UpdateOneAsync(
            filter,
            update,
            RefundUpdateOptions(refundId),
            cancellationToken);
    }

    public async Task<bool> ApplyProviderEventAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        IReadOnlyCollection<string> expectedStatuses,
        string targetStatus,
        string providerRefundReference,
        DateTime eventDateUtc,
        decimal reservedAmountDelta,
        decimal refundedAmountDelta,
        string targetPaymentStatus,
        string? completionAction,
        string? failureCode,
        string? failureSummary,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.ItemId,
                paymentDetailId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.TenantId,
                tenantId),
            Builders<PaymentDetail>.Filter.ElemMatch(
                payment => payment.Refunds,
                refund =>
                    refund.RefundId == refundId &&
                    expectedStatuses.Contains(
                        refund.Status) &&
                    (refund.LastProviderEventAtUtc == null ||
                     refund.LastProviderEventAtUtc <=
                     eventDateUtc) &&
                    !refund.OutboxEvents.Any(
                        item =>
                            item.DeduplicationKey ==
                            outboxEvent
                                .DeduplicationKey)));
        var set = new BsonDocument
        {
            ["Refunds.$[refund].Status"] =
                targetStatus,
            ["Refunds.$[refund].ProviderRefundReference"] =
                providerRefundReference,
            ["Refunds.$[refund].CompletionAction"] =
                completionAction == null
                    ? BsonNull.Value
                    : completionAction,
            ["Refunds.$[refund].FailureCode"] =
                failureCode == null
                    ? BsonNull.Value
                    : failureCode,
            ["Refunds.$[refund].FailureSummary"] =
                failureSummary == null
                    ? BsonNull.Value
                    : failureSummary,
            ["Refunds.$[refund].LastProviderEventAtUtc"] =
                eventDateUtc,
            ["Refunds.$[refund].CompletedAtUtc"] =
                eventDateUtc,
            ["Refunds.$[refund].UpdatedAtUtc"] =
                DateTime.UtcNow,
            ["Refunds.$[refund].ProcessingLeaseId"] =
                BsonNull.Value,
            ["Refunds.$[refund].ProcessingLeaseExpiresAtUtc"] =
                BsonNull.Value,
            ["PaymentStatus"] = targetPaymentStatus,
            ["LastUpdatedDateUtc"] = DateTime.UtcNow
        };
        var update = new BsonDocument
        {
            ["$set"] = set,
            ["$inc"] = new BsonDocument
            {
                ["ReservedRefundAmount"] =
                    new Decimal128(
                        reservedAmountDelta),
                ["RefundedAmount"] =
                    new Decimal128(
                        refundedAmountDelta)
            },
            ["$push"] = new BsonDocument(
                "Refunds.$[refund].OutboxEvents",
                outboxEvent.ToBsonDocument())
        };

        var result = await Payments(tenantId).UpdateOneAsync(
            filter,
            update,
            RefundUpdateOptions(refundId),
            cancellationToken);

        return result.ModifiedCount == 1;
    }

    public Task<List<PaymentDetail>>
        GetPaymentsWithDueRefundInitiationsAsync(
            string tenantId,
            DateTime utcNow,
            int limit,
            CancellationToken cancellationToken)
    {
        var refundFilter =
            Builders<PaymentRefund>.Filter.And(
                Builders<PaymentRefund>.Filter.In(
                    refund => refund.Status,
                    [
                        PaymentRefundStatuses.Initiating,
                        PaymentRefundStatuses
                            .InitiationUnknown
                    ]),
                Builders<PaymentRefund>.Filter.Lte(
                    refund =>
                        refund.NextRecoveryAttemptAtUtc,
                    utcNow),
                Builders<PaymentRefund>.Filter.Or(
                    Builders<PaymentRefund>.Filter.Eq(
                        refund =>
                            refund.ProcessingLeaseExpiresAtUtc,
                        null),
                    Builders<PaymentRefund>.Filter.Lte(
                        refund =>
                            refund.ProcessingLeaseExpiresAtUtc,
                        utcNow)));

        return Payments(tenantId)
            .Find(
                Builders<PaymentDetail>.Filter.And(
                    Builders<PaymentDetail>.Filter.Eq(
                        payment => payment.TenantId,
                        tenantId),
                    Builders<PaymentDetail>.Filter.ElemMatch(
                        payment => payment.Refunds,
                        refundFilter)))
            .SortBy(payment => payment.LastUpdatedDateUtc)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public Task<List<PaymentDetail>>
        GetPaymentsWithDueRefundOutboxEventsAsync(
            string tenantId,
            DateTime utcNow,
            int limit,
            CancellationToken cancellationToken)
    {
        var eventFilter =
            Builders<PaymentOutboxEvent>.Filter.And(
                Builders<PaymentOutboxEvent>.Filter.In(
                    item => item.Status,
                    [
                        PaymentOutboxStatus.Pending,
                        PaymentOutboxStatus.RetryScheduled,
                        PaymentOutboxStatus.Processing
                    ]),
                Builders<PaymentOutboxEvent>.Filter.Lte(
                    item => item.NextAttemptAtUtc,
                    utcNow),
                Builders<PaymentOutboxEvent>.Filter.Or(
                    Builders<PaymentOutboxEvent>.Filter.Ne(
                        item => item.Status,
                        PaymentOutboxStatus.Processing),
                    Builders<PaymentOutboxEvent>.Filter.Lte(
                        item => item.LeaseExpiresAtUtc,
                        utcNow)));
        var refundFilter =
            Builders<PaymentRefund>.Filter.ElemMatch(
                refund => refund.OutboxEvents,
                eventFilter);

        return Payments(tenantId)
            .Find(
                Builders<PaymentDetail>.Filter.And(
                    Builders<PaymentDetail>.Filter.Eq(
                        payment => payment.TenantId,
                        tenantId),
                    Builders<PaymentDetail>.Filter.ElemMatch(
                        payment => payment.Refunds,
                        refundFilter)))
            .SortBy(payment => payment.LastUpdatedDateUtc)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimOutboxEventAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string eventId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = new BsonDocument
        {
            ["_id"] = paymentDetailId,
            ["TenantId"] = tenantId,
            ["Refunds"] = new BsonDocument(
                "$elemMatch",
                new BsonDocument
                {
                    ["RefundId"] = refundId,
                    ["OutboxEvents"] = new BsonDocument(
                        "$elemMatch",
                        new BsonDocument
                        {
                            ["EventId"] = eventId,
                            ["NextAttemptAtUtc"] =
                                new BsonDocument(
                                    "$lte",
                                    now)
                        }),
                    ["$or"] = new BsonArray
                    {
                        new BsonDocument(
                            "OutboxEvents",
                            new BsonDocument(
                                "$elemMatch",
                                new BsonDocument
                                {
                                    ["EventId"] = eventId,
                                    ["Status"] =
                                        new BsonDocument(
                                            "$in",
                                            new BsonArray
                                            {
                                                (int)
                                                PaymentOutboxStatus
                                                    .Pending,
                                                (int)
                                                PaymentOutboxStatus
                                                    .RetryScheduled
                                            }),
                                    ["NextAttemptAtUtc"] =
                                        new BsonDocument(
                                            "$lte",
                                            now)
                                })),
                        new BsonDocument(
                            "OutboxEvents",
                            new BsonDocument(
                                "$elemMatch",
                                new BsonDocument
                                {
                                    ["EventId"] = eventId,
                                    ["Status"] =
                                        (int)
                                        PaymentOutboxStatus
                                            .Processing,
                                    ["NextAttemptAtUtc"] =
                                        new BsonDocument(
                                            "$lte",
                                            now),
                                    ["LeaseExpiresAtUtc"] =
                                        new BsonDocument(
                                            "$lte",
                                            now)
                                }))
                    }
                })
        };
        var update = new BsonDocument(
            "$set",
            new BsonDocument
            {
                ["Refunds.$[refund].OutboxEvents.$[event].Status"] =
                    (int)PaymentOutboxStatus.Processing,
                ["Refunds.$[refund].OutboxEvents.$[event].LeaseId"] =
                    leaseId,
                ["Refunds.$[refund].OutboxEvents.$[event].LeaseExpiresAtUtc"] =
                    leaseExpiresAtUtc
            });

        var result = await Payments(tenantId).UpdateOneAsync(
            filter,
            update,
            RefundEventUpdateOptions(
                refundId,
                eventId),
            cancellationToken);

        return result.ModifiedCount == 1;
    }

    public Task MarkOutboxPublishedAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string eventId,
        string leaseId,
        DateTime publishedAtUtc,
        CancellationToken cancellationToken) =>
        UpdateOutboxAsync(
            tenantId,
            paymentDetailId,
            refundId,
            eventId,
            leaseId,
            new BsonDocument
            {
                ["Refunds.$[refund].OutboxEvents.$[event].Status"] =
                    (int)PaymentOutboxStatus.Published,
                ["Refunds.$[refund].OutboxEvents.$[event].PublishedAtUtc"] =
                    publishedAtUtc,
                ["Refunds.$[refund].OutboxEvents.$[event].LeaseId"] =
                    BsonNull.Value,
                ["Refunds.$[refund].OutboxEvents.$[event].LeaseExpiresAtUtc"] =
                    BsonNull.Value
            },
            cancellationToken);

    public Task MarkOutboxFailedAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string eventId,
        string leaseId,
        PaymentOutboxStatus status,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string safeError,
        CancellationToken cancellationToken) =>
        UpdateOutboxAsync(
            tenantId,
            paymentDetailId,
            refundId,
            eventId,
            leaseId,
            new BsonDocument
            {
                ["Refunds.$[refund].OutboxEvents.$[event].Status"] =
                    (int)status,
                ["Refunds.$[refund].OutboxEvents.$[event].AttemptCount"] =
                    attemptCount,
                ["Refunds.$[refund].OutboxEvents.$[event].NextAttemptAtUtc"] =
                    nextAttemptAtUtc,
                ["Refunds.$[refund].OutboxEvents.$[event].LastError"] =
                    Sanitize(safeError),
                ["Refunds.$[refund].OutboxEvents.$[event].LeaseId"] =
                    BsonNull.Value,
                ["Refunds.$[refund].OutboxEvents.$[event].LeaseExpiresAtUtc"] =
                    BsonNull.Value
            },
            cancellationToken);

    private Task UpdateOutboxAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string eventId,
        string leaseId,
        BsonDocument values,
        CancellationToken cancellationToken) =>
        Payments(tenantId).UpdateOneAsync(
            Builders<PaymentDetail>.Filter.And(
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.ItemId,
                    paymentDetailId),
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.TenantId,
                    tenantId),
                Builders<PaymentDetail>.Filter.ElemMatch(
                    payment => payment.Refunds,
                    refund =>
                        refund.RefundId == refundId &&
                        refund.OutboxEvents.Any(
                            item =>
                                item.EventId == eventId &&
                                item.LeaseId == leaseId))),
            new BsonDocument("$set", values),
            RefundEventUpdateOptions(
                refundId,
                eventId),
            cancellationToken);

    private static FilterDefinition<PaymentDetail>
        RefundLeaseFilter(
            string tenantId,
            string paymentDetailId,
            string refundId,
            string leaseId) =>
        Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.ItemId,
                paymentDetailId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.TenantId,
                tenantId),
            Builders<PaymentDetail>.Filter.ElemMatch(
                payment => payment.Refunds,
                refund =>
                    refund.RefundId == refundId &&
                    refund.ProcessingLeaseId == leaseId));

    private static BsonDocumentArrayFilterDefinition<
        PaymentRefund> RefundArrayFilter(string refundId) =>
        new(new BsonDocument(
            "refund.RefundId",
            refundId));

    private static UpdateOptions RefundUpdateOptions(
        string refundId) =>
        new()
        {
            ArrayFilters =
            [
                RefundArrayFilter(refundId)
            ]
        };

    private static UpdateOptions RefundEventUpdateOptions(
        string refundId,
        string eventId) =>
        new()
        {
            ArrayFilters =
            [
                RefundArrayFilter(refundId),
                new BsonDocumentArrayFilterDefinition<
                    PaymentOutboxEvent>(
                    new BsonDocument(
                        "event.EventId",
                        eventId))
            ]
        };

    private IMongoCollection<PaymentDetail> Payments(
        string tenantId) =>
        _dbContextProvider
            .GetDatabase(RequireTenant(tenantId))
            .GetCollection<PaymentDetail>(
                "PaymentDetails");

    private static string RequireTenant(
        string tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId)
            ? tenantId
            : throw new InvalidOperationException(
                "A tenant id is required.");

    private static string Sanitize(string value) =>
        value.Length <= 200
            ? value
            : value[..200];
}
