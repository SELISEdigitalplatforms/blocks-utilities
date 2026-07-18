using MongoDB.Bson;
using MongoDB.Driver;
using Payment.DomainService.Entities;

namespace Payment.DomainService.Repositories;

public static class PaymentIndexDefinitions
{
    public const string LegacyOutboxDeduplicationIndexName =
        "ux_payment_tenant_outbox_deduplication";

    public const string OutboxDeduplicationIndexName =
        "ux_payment_outbox_deduplication_tenant_partial";

    public static IReadOnlyCollection<CreateIndexModel<PaymentDetail>> Create() =>
    [
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.IdempotencyKey),
            new CreateIndexOptions
            {
                Unique = true,
                Name = "ux_payment_tenant_idempotency"
            }),
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending(x => x.PaymentStatus)
                .Ascending(x => x.ProcessingLeaseExpiresAtUtc),
            new CreateIndexOptions
            {
                Name = "ix_payment_status_lease"
            }),
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.PspReference),
            new CreateIndexOptions<PaymentDetail>
            {
                Unique = true,
                Name = "ux_payment_tenant_psp_reference",
                PartialFilterExpression = new BsonDocument(
                    nameof(PaymentDetail.PspReference),
                    new BsonDocument("$type", "string"))
            }),
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending("OutboxEvents.Status")
                .Ascending("OutboxEvents.NextAttemptAtUtc"),
            new CreateIndexOptions
            {
                Name = "ix_payment_outbox_due"
            }),
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending("OutboxEvents.DeduplicationKey")
                .Ascending(x => x.TenantId),
            new CreateIndexOptions<PaymentDetail>
            {
                Unique = true,
                Name = OutboxDeduplicationIndexName,
                PartialFilterExpression = new BsonDocument(
                    "OutboxEvents.DeduplicationKey",
                    new BsonDocument("$type", "string"))
            })
    ];
}
