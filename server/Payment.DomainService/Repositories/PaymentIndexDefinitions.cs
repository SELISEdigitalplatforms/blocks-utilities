using MongoDB.Bson;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public static class PaymentIndexDefinitions
{
    public const string LegacyOutboxDeduplicationIndexName =
        "ux_payment_tenant_outbox_deduplication";

    public const string OutboxDeduplicationIndexName =
        "ux_payment_outbox_deduplication_tenant_partial";

    public const string RecurringOrderIndexName =
        "ux_payment_recurring_tenant_order";

    public const string PaymentQueryDateIndexName =
        "ix_payment_query_tenant_date_id";

    public const string PaymentQueryProviderIndexName =
        "ix_payment_query_tenant_provider_id";

    public const string PaymentQueryAmountIndexName =
        "ix_payment_query_tenant_amount_id";

    public const string PaymentQueryStatusIndexName =
        "ix_payment_query_tenant_status_id";

    public const string SubscriptionInvoiceHistoryIndexName =
        "ix_payment_subscription_invoice_history";

    public const string ProviderMerchantIndexName =
        "ux_payment_provider_tenant_org_provider_merchant";

    /// <summary>
    /// Replaced when configurations became organization-scoped. Dropped on first use.
    /// </summary>
    public const string LegacyProviderMerchantIndexName =
        "ux_payment_provider_tenant_provider_merchant";

    /// <summary>
    /// One provider configuration per tenant, organization, provider and merchant. Enforced in
    /// the database rather than by a read-then-write, so two concurrent registrations cannot
    /// both succeed.
    /// </summary>
    /// <remarks>
    /// Organizations within a tenant may be separate businesses with their own merchant
    /// accounts, so each needs its own configuration. A null organization is a legitimate key
    /// here, not an absent one: it is the tenant-level configuration that predates scoping and
    /// that provider resolution falls back to.
    /// </remarks>
    public static IReadOnlyCollection<CreateIndexModel<PaymentProvider>> CreateProviderIndexes() =>
    [
        new(
            Builders<PaymentProvider>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.OrganizationId)
                .Ascending(x => x.ProviderName)
                .Ascending(x => x.MerchantId),
            new CreateIndexOptions
            {
                Unique = true,
                Name = ProviderMerchantIndexName
            })
    ];

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
                .Ascending(x => x.PaymentDate)
                .Ascending(x => x.ItemId),
            new CreateIndexOptions
            {
                Name = PaymentQueryDateIndexName
            }),
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.ProviderName)
                .Ascending(x => x.ItemId),
            new CreateIndexOptions
            {
                Name = PaymentQueryProviderIndexName
            }),
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.PreciseAmount)
                .Ascending(x => x.ItemId),
            new CreateIndexOptions
            {
                Name = PaymentQueryAmountIndexName
            }),
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.PaymentStatus)
                .Ascending(x => x.ItemId),
            new CreateIndexOptions
            {
                Name = PaymentQueryStatusIndexName
            }),
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending(payment => payment.TenantId)
                .Ascending(payment => payment.CustomerOrganizationId)
                .Descending(payment => payment.PaymentDate)
                .Descending(payment => payment.ItemId),
            new CreateIndexOptions<PaymentDetail>
            {
                Name = SubscriptionInvoiceHistoryIndexName,
                PartialFilterExpression = new BsonDocument(
                    nameof(PaymentDetail.PaymentFlow),
                    PaymentFlows.SubscriptionInvoice)
            }),
        new(
            Builders<PaymentDetail>.IndexKeys
                .Ascending(payment => payment.TenantId)
                .Ascending(payment => payment.OrderId),
            new CreateIndexOptions<PaymentDetail>
            {
                Unique = true,
                Name = RecurringOrderIndexName,
                PartialFilterExpression =
                    new BsonDocument(
                        nameof(PaymentDetail.PaymentFlow),
                        PaymentFlows.RecurringCharge)
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
