using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The indexes this module relies on for correctness, not only for speed.
/// </summary>
/// <remarks>
/// Kept separate from the payment module's definitions on purpose. Payment guards index
/// creation with a per-process record of which tenants it has already done; adding subscription
/// indexes there would mean any tenant that process had already touched never gets them, and
/// the first sign would be duplicate billing in production.
/// </remarks>
public static class SubscriptionIndexDefinitions
{
    public const string ActiveSubscriptionIndexName =
        "ux_subscription_tenant_org_live";

    public const string SubscriptionOrganizationIndexName =
        "ix_subscription_tenant_org_status";

    public const string SubscriptionOrderIndexName =
        "ix_subscription_tenant_order";

    public const string SubscriptionRenewalDueIndexName =
        "ix_subscription_tenant_status_next_fee_billing";

    public const string PlanCodeIndexName =
        "ux_subscription_plan_tenant_org_code";

    public const string PricePlanIndexName =
        "ix_subscription_price_tenant_plan";

    public const string BillingAccountIndexName =
        "ux_subscription_account_tenant_org_provider";

    public const string UsageIdempotencyIndexName =
        "ux_subscription_usage_tenant_subscription_key";

    public const string UsagePeriodIndexName =
        "ix_subscription_usage_tenant_subscription_meter_period";

    public const string UsageCounterExpiryIndexName =
        "ttl_subscription_usage_counter_expires";

    public const string PaymentLinkPaymentIndexName =
        "ux_subscription_link_tenant_payment";

    public const string PaymentLinkSweepIndexName =
        "ix_subscription_link_tenant_state_next_check";

    public const string UsageInvoiceUniqueIndexName =
        "ux_subscription_usageinvoice_tenant_subscription_period";

    public const string UsageInvoiceSweepIndexName =
        "ix_subscription_usageinvoice_tenant_state_next_attempt";

    /// <summary>
    /// One live subscription per organization, enforced by the database rather than by a
    /// read-then-write, so two concurrent signups cannot both succeed.
    /// </summary>
    /// <remarks>
    /// Partial on the statuses that grant something: ended subscriptions must be allowed to
    /// accumulate, or an organization could never resubscribe after cancelling.
    /// </remarks>
    public static IReadOnlyCollection<CreateIndexModel<SubscriptionDetail>> CreateSubscriptionIndexes() =>
    [
        new(
            Builders<SubscriptionDetail>.IndexKeys
                .Ascending(subscription => subscription.TenantId)
                .Ascending(subscription => subscription.OrganizationId),
            new CreateIndexOptions<SubscriptionDetail>
            {
                Unique = true,
                Name = ActiveSubscriptionIndexName,
                PartialFilterExpression = new BsonDocument(
                    nameof(SubscriptionDetail.Status),
                    new BsonDocument(
                        "$in",
                        new BsonArray
                        {
                            (int)SubscriptionStatus.Trialing,
                            (int)SubscriptionStatus.Active,
                            (int)SubscriptionStatus.PastDue
                        }))
            }),
        new(
            Builders<SubscriptionDetail>.IndexKeys
                .Ascending(subscription => subscription.TenantId)
                .Ascending(subscription => subscription.OrganizationId)
                .Ascending(subscription => subscription.Status),
            new CreateIndexOptions { Name = SubscriptionOrganizationIndexName }),
        new(
            Builders<SubscriptionDetail>.IndexKeys
                .Ascending(subscription => subscription.TenantId)
                .Ascending(subscription => subscription.OrderId),
            new CreateIndexOptions { Name = SubscriptionOrderIndexName }),
        // What the renewal sweep queries: every subscription that could possibly be due,
        // narrowed by status first since most subscriptions are not near their billing date.
        new(
            Builders<SubscriptionDetail>.IndexKeys
                .Ascending(subscription => subscription.TenantId)
                .Ascending(subscription => subscription.Status)
                .Ascending(subscription => subscription.NextFeeBillingAtUtc),
            new CreateIndexOptions { Name = SubscriptionRenewalDueIndexName })
    ];

    public static IReadOnlyCollection<CreateIndexModel<Plan>> CreatePlanIndexes() =>
    [
        new(
            Builders<Plan>.IndexKeys
                .Ascending(plan => plan.TenantId)
                .Ascending(plan => plan.OrganizationId)
                .Ascending(plan => plan.Code),
            new CreateIndexOptions
            {
                Unique = true,
                Name = PlanCodeIndexName
            })
    ];

    public static IReadOnlyCollection<CreateIndexModel<Price>> CreatePriceIndexes() =>
    [
        new(
            Builders<Price>.IndexKeys
                .Ascending(price => price.TenantId)
                .Ascending(price => price.PlanId),
            new CreateIndexOptions { Name = PricePlanIndexName })
    ];

    public static IReadOnlyCollection<CreateIndexModel<BillingAccount>> CreateBillingAccountIndexes() =>
    [
        new(
            Builders<BillingAccount>.IndexKeys
                .Ascending(account => account.TenantId)
                .Ascending(account => account.OrganizationId)
                .Ascending(account => account.ProviderName),
            new CreateIndexOptions
            {
                Unique = true,
                Name = BillingAccountIndexName
            })
    ];

    /// <summary>
    /// The guard against billing a customer twice for one event.
    /// </summary>
    /// <remarks>
    /// Callers retry — a timeout, a redelivery, a double click — and at-least-once delivery
    /// makes that a certainty rather than a risk. Uniqueness here is what turns a retry into a
    /// no-op instead of a second charge.
    /// </remarks>
    public static IReadOnlyCollection<CreateIndexModel<SubscriptionUsageRecord>> CreateUsageRecordIndexes() =>
    [
        new(
            Builders<SubscriptionUsageRecord>.IndexKeys
                .Ascending(record => record.TenantId)
                .Ascending(record => record.SubscriptionId)
                .Ascending(record => record.IdempotencyKey),
            new CreateIndexOptions
            {
                Unique = true,
                Name = UsageIdempotencyIndexName
            }),
        new(
            Builders<SubscriptionUsageRecord>.IndexKeys
                .Ascending(record => record.TenantId)
                .Ascending(record => record.SubscriptionId)
                .Ascending(record => record.MeterKey)
                .Ascending(record => record.PeriodKey),
            new CreateIndexOptions { Name = UsagePeriodIndexName })
    ];

    /// <summary>
    /// Counters expire; the ledger behind them does not. A counter is a derived read model and
    /// can always be recomputed, whereas the ledger is the record that has to explain a bill.
    /// </summary>
    public static IReadOnlyCollection<CreateIndexModel<SubscriptionUsageCounter>> CreateUsageCounterIndexes() =>
    [
        new(
            Builders<SubscriptionUsageCounter>.IndexKeys
                .Ascending(counter => counter.ExpiresAtUtc),
            new CreateIndexOptions
            {
                Name = UsageCounterExpiryIndexName,
                ExpireAfter = TimeSpan.Zero
            })
    ];

    public static IReadOnlyCollection<CreateIndexModel<SubscriptionPaymentLink>> CreatePaymentLinkIndexes() =>
    [
        new(
            Builders<SubscriptionPaymentLink>.IndexKeys
                .Ascending(link => link.TenantId)
                .Ascending(link => link.PaymentDetailId),
            new CreateIndexOptions
            {
                Unique = true,
                Name = PaymentLinkPaymentIndexName
            }),
        new(
            Builders<SubscriptionPaymentLink>.IndexKeys
                .Ascending(link => link.TenantId)
                .Ascending(link => link.State)
                .Ascending(link => link.NextCheckAtUtc),
            new CreateIndexOptions { Name = PaymentLinkSweepIndexName })
    ];

    /// <summary>
    /// The double-billing guard for usage rating: one invoice per subscription per usage period,
    /// enforced by the database rather than a read-then-write.
    /// </summary>
    public static IReadOnlyCollection<CreateIndexModel<SubscriptionUsageInvoice>> CreateUsageInvoiceIndexes() =>
    [
        new(
            Builders<SubscriptionUsageInvoice>.IndexKeys
                .Ascending(invoice => invoice.TenantId)
                .Ascending(invoice => invoice.SubscriptionId)
                .Ascending(invoice => invoice.PeriodKey),
            new CreateIndexOptions
            {
                Unique = true,
                Name = UsageInvoiceUniqueIndexName
            }),
        new(
            Builders<SubscriptionUsageInvoice>.IndexKeys
                .Ascending(invoice => invoice.TenantId)
                .Ascending(invoice => invoice.State)
                .Ascending(invoice => invoice.NextAttemptAtUtc),
            new CreateIndexOptions { Name = UsageInvoiceSweepIndexName })
    ];
}
