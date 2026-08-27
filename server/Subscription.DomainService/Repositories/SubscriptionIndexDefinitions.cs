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
    /// <summary>
    /// Versioned because MongoDB does not replace an existing named index when its partial
    /// filter changes. Deploying this alongside the legacy live-only index upgrades existing
    /// tenant databases as they are first touched.
    /// </summary>
    public const string SubscriptionReservationIndexName =
        "ux_subscription_tenant_org_reserved_v2";

    public const string SubscriptionOrganizationIndexName =
        "ix_subscription_tenant_org_status";

    public const string SubscriptionOrderIndexName =
        "ix_subscription_tenant_order";

    public const string SubscriptionRenewalDueIndexName =
        "ix_subscription_tenant_status_next_fee_billing";

    public const string SubscriptionUsageRatingDueIndexName =
        "ix_subscription_tenant_status_next_usage_billing";

    public const string PlanCodeIndexName =
        "ux_subscription_plan_tenant_org_code";

    public const string PricePlanIndexName =
        "ix_subscription_price_tenant_plan";
    public const string DiscountCodeIndexName = "ux_subscription_discount_tenant_org_code";

    public const string BillingAccountIndexName =
        "ux_subscription_account_tenant_org_provider";

    public const string BillingProfileIndexName =
        "ux_subscription_billing_profile_tenant_org";

    public const string MerchantProfileIndexName =
        "ux_subscription_merchant_profile_tenant";

    public const string DocumentSourceIndexName =
        "ix_subscription_document_sources_pending";

    public const string TrialStartIndexName =
        "ix_subscription_trial_start";

    /// <summary>
    /// What makes issuing a financial document exactly-once. Every other index here is for speed;
    /// this one is the correctness guarantee, and losing it means duplicate invoice numbers and
    /// duplicate emails for money that moved once.
    /// </summary>
    public const string FinancialDocumentSourceIndexName =
        "ux_subscription_document_tenant_source";

    public const string FinancialDocumentNumberIndexName =
        "ux_subscription_document_tenant_number";

    public const string FinancialDocumentOrganizationIndexName =
        "ix_subscription_document_tenant_org_issued";

    public const string FinancialDocumentSubscriptionIndexName =
        "ix_subscription_document_tenant_subscription_issued";

    public const string FinancialDocumentDeliveryIndexName =
        "ix_subscription_document_tenant_delivery_state";

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
    /// One open subscription attempt per organization, enforced before checkout by the database
    /// rather than by a read-then-write, so two concurrent signups cannot both reach payment.
    /// </summary>
    /// <remarks>
    /// Includes <see cref="SubscriptionStatus.Incomplete"/> because that is the status inserted
    /// before checkout. Restricting the old index to granting statuses allowed another checkout
    /// while an organization was already subscribed; the conflict appeared only during
    /// activation, after money had moved. Ended subscriptions remain outside the reservation so
    /// an organization can subscribe again after the prior attempt or subscription ends.
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
                Name = SubscriptionReservationIndexName,
                PartialFilterExpression = new BsonDocument(
                    nameof(SubscriptionDetail.Status),
                    new BsonDocument(
                        "$in",
                        new BsonArray
                        {
                            (int)SubscriptionStatus.Incomplete,
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
            new CreateIndexOptions { Name = SubscriptionRenewalDueIndexName }),
        new(
            Builders<SubscriptionDetail>.IndexKeys
                .Ascending(subscription => subscription.TenantId)
                .Ascending(subscription => subscription.Status)
                .Ascending(subscription => subscription.NextUsageBillingAtUtc),
            new CreateIndexOptions { Name = SubscriptionUsageRatingDueIndexName }),
        // What the document-recovery sweep queries. Partial on the array being non-empty, which is
        // what lets that sweep run with no time window at all: the index holds only the handful of
        // subscriptions that currently owe a document, so asking "which ones, ever?" costs the same
        // as asking "which ones in the last hour?" and cannot miss an obligation older than a guess.
        new(
            Builders<SubscriptionDetail>.IndexKeys
                .Ascending(subscription => subscription.TenantId)
                .Ascending(subscription => subscription.LastUpdatedDateUtc),
            new CreateIndexOptions<SubscriptionDetail>
            {
                Name = DocumentSourceIndexName,
                PartialFilterExpression = new BsonDocument(
                    $"{nameof(SubscriptionDetail.PendingDocumentSources)}.0",
                    new BsonDocument("$exists", true))
            }),
        // What the trial-document backstop walks. Partial on there being a trial at all, because most
        // subscriptions never had one and an index over them would be mostly empty keys. The id is in
        // the key because the sweep pages on (start, id) — without it, a page of trials sharing one
        // start instant would need a blocking sort to resume from.
        new(
            Builders<SubscriptionDetail>.IndexKeys
                .Ascending(subscription => subscription.TenantId)
                .Ascending("Trial.StartsAtUtc")
                .Ascending(subscription => subscription.ItemId),
            new CreateIndexOptions<SubscriptionDetail>
            {
                Name = TrialStartIndexName,
                PartialFilterExpression = new BsonDocument(
                    nameof(SubscriptionDetail.Trial),
                    new BsonDocument("$exists", true))
            })
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

    public static IReadOnlyCollection<CreateIndexModel<Discount>> CreateDiscountIndexes() =>
    [
        new(
            Builders<Discount>.IndexKeys
                .Ascending(discount => discount.TenantId)
                .Ascending(discount => discount.OrganizationId)
                .Ascending(discount => discount.Code),
            new CreateIndexOptions { Unique = true, Name = DiscountCodeIndexName })
    ];

    public static IReadOnlyCollection<CreateIndexModel<SubscriptionBillingProfile>>
        CreateBillingProfileIndexes() =>
    [
        new(
            Builders<SubscriptionBillingProfile>.IndexKeys
                .Ascending(profile => profile.TenantId)
                .Ascending(profile => profile.OrganizationId),
            new CreateIndexOptions { Unique = true, Name = BillingProfileIndexName })
    ];

    /// <summary>
    /// One selling identity per tenant, enforced rather than assumed.
    /// </summary>
    /// <remarks>
    /// Unique on the tenant alone: a second merchant profile would mean two answers to who issued an
    /// invoice, and the upsert that maintains it would silently pick one of them.
    /// </remarks>
    public static IReadOnlyCollection<CreateIndexModel<SubscriptionMerchantProfile>>
        CreateMerchantProfileIndexes() =>
    [
        new(
            Builders<SubscriptionMerchantProfile>.IndexKeys
                .Ascending(profile => profile.TenantId),
            new CreateIndexOptions { Unique = true, Name = MerchantProfileIndexName })
    ];

    /// <summary>
    /// The ledger's indexes. The first two are unique and are the only reason issuing a document is
    /// safe under concurrency; the rest are what make the history endpoints and the delivery sweep
    /// affordable.
    /// </summary>
    public static IReadOnlyCollection<CreateIndexModel<SubscriptionFinancialDocument>>
        CreateFinancialDocumentIndexes() =>
    [
        new(
            Builders<SubscriptionFinancialDocument>.IndexKeys
                .Ascending(document => document.TenantId)
                .Ascending(document => document.SourceKey),
            new CreateIndexOptions
            {
                Unique = true,
                Name = FinancialDocumentSourceIndexName
            }),
        new(
            Builders<SubscriptionFinancialDocument>.IndexKeys
                .Ascending(document => document.TenantId)
                .Ascending(document => document.DocumentNumber),
            new CreateIndexOptions
            {
                Unique = true,
                Name = FinancialDocumentNumberIndexName
            }),
        // Issue date descending because every listing is newest-first, and the tie-break is the id
        // so a cursor can page without skipping documents issued in the same millisecond.
        new(
            Builders<SubscriptionFinancialDocument>.IndexKeys
                .Ascending(document => document.TenantId)
                .Ascending(document => document.OrganizationId)
                .Descending(document => document.IssuedAtUtc)
                .Descending(document => document.ItemId),
            new CreateIndexOptions { Name = FinancialDocumentOrganizationIndexName }),
        new(
            Builders<SubscriptionFinancialDocument>.IndexKeys
                .Ascending(document => document.TenantId)
                .Ascending(document => document.SubscriptionId)
                .Descending(document => document.IssuedAtUtc),
            new CreateIndexOptions { Name = FinancialDocumentSubscriptionIndexName }),
        // Partial, so the sweep reads a small index rather than every document ever issued. The
        // overwhelming majority are delivered and will never be looked at again.
        new(
            Builders<SubscriptionFinancialDocument>.IndexKeys
                .Ascending(document => document.TenantId)
                .Ascending(document => document.CreatedAtUtc),
            new CreateIndexOptions<SubscriptionFinancialDocument>
            {
                Name = FinancialDocumentDeliveryIndexName,
                PartialFilterExpression = new BsonDocument(
                    "Delivery.State",
                    new BsonDocument(
                        "$in",
                        new BsonArray
                        {
                            (int)FinancialDocumentDeliveryState.Pending,
                            (int)FinancialDocumentDeliveryState.Generated
                        }))
            })
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

    public const string UsagePeriodClaimLookupIndexName =
        "ix_usage_period_claim_tenant_subscription_period";

    /// <summary>
    /// Not for uniqueness — <c>ItemId</c> already guarantees one claim per idempotency key on its
    /// own — but for a future recovery sweep to find every claim against one period without a
    /// collection scan.
    /// </summary>
    public static IReadOnlyCollection<CreateIndexModel<UsagePeriodClaim>> CreateUsagePeriodClaimIndexes() =>
    [
        new(
            Builders<UsagePeriodClaim>.IndexKeys
                .Ascending(claim => claim.TenantId)
                .Ascending(claim => claim.SubscriptionId)
                .Ascending(claim => claim.PeriodKey),
            new CreateIndexOptions { Name = UsagePeriodClaimLookupIndexName })
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
