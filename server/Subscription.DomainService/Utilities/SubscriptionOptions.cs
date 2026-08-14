namespace Subscription.DomainService.Utilities;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscription";

    /// <summary>
    /// How long a resolved subscription may be served from memory before it is read again.
    /// Entitlement tolerates this staleness deliberately; usage counters never do and are
    /// never cached.
    /// </summary>
    public int EntitlementCacheSeconds { get; set; } = 10;

    /// <summary>
    /// How long a finished usage period's counter is kept after it ends. The counter is a
    /// derived read model and can always be rebuilt from the ledger, which is never expired.
    /// </summary>
    public int CounterRetentionDays { get; set; } = 400;

    /// <summary>
    /// How long a subscription may sit unpaid before the initial charge is considered
    /// abandoned. Covers the shopper who opens checkout and closes the tab.
    /// </summary>
    public int InitialChargeGraceMinutes { get; set; } = 60;

    /// <summary>
    /// How often the worker sweeps for subscriptions whose activation never completed. Without
    /// a tick, a subscription whose activation lost a compare-and-set stays unpaid-looking
    /// forever while the shopper's money has already moved.
    /// </summary>
    public int ReconciliationPollSeconds { get; set; } = 120;

    public int ActivationBatchSize { get; set; } = 50;
    public int ActivationMaxAttempts { get; set; } = 10;
    public int ActivationRetrySeconds { get; set; } = 30;

    public int OutboxBatchSize { get; set; } = 50;
    public int OutboxLeaseSeconds { get; set; } = 30;
    public int OutboxMaxAttempts { get; set; } = 10;

    public int UsageRequestsPerMinute { get; set; } = 600;
    public int EntitlementRequestsPerMinute { get; set; } = 1_200;

    /// <summary>
    /// Caps on the free-form metadata a usage record may carry. Billing needs a count, not a
    /// dossier: without a bound this field becomes an unversioned side-channel for whatever
    /// the calling product happens to hold, including personal data it should not put here.
    /// </summary>
    public int MaximumUsageMetadataEntries { get; set; } = 10;
    public int MaximumUsageMetadataValueLength { get; set; } = 256;

    /// <summary>
    /// Which tenants background sweeps run for. Nothing else discovers tenants, so a tenant
    /// omitted here is silently skipped.
    /// </summary>
    public string[] TenantIds { get; set; } = [];
}
