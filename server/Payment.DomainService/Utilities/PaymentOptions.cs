namespace Payment.DomainService.Utilities;

public sealed class PaymentOptions
{
    public const string SectionName = "Payment";
    public int ProviderTimeoutSeconds { get; set; } = 15;
    public int ProcessingLeaseSeconds { get; set; } = 30;
    public int DistributedLockSeconds { get; set; } = 20;
    public int DistributedLockWaitMilliseconds { get; set; } = 750;
    public int TenantRequestsPerMinute { get; set; } = 300;
    public int ActorRequestsPerMinute { get; set; } = 30;
    public int OrderRequestsPerMinute { get; set; } = 10;
    public int ProviderCacheSeconds { get; set; } = 120;
    public int ProviderSecretRefreshThrottleSeconds { get; set; } = 30;
    public int OutboxBatchSize { get; set; } = 50;
    public int ReconciliationPollSeconds { get; set; } = 300;

    /// <summary>
    /// Whether the durable work queue drives payment background work.
    /// </summary>
    /// <remarks>
    /// Off by default. Turned on, a worker schedules recovery and outbox work per tenant and drains
    /// it from the root database instead of walking the roster inline.
    /// <para>
    /// There is no second executor to disagree with, unlike the subscription side: the payment
    /// reconciliation sweep this replaces has been disabled — its loop is commented out and it logs
    /// only that the safety net is off — so turning this on restores recovery rather than moving it.
    /// </para>
    /// </remarks>
    public bool SchedulerEnabled { get; set; }

    /// <summary>How often a worker asks the queue for due work.</summary>
    public int SchedulerPollSeconds { get; set; } = 10;

    public int SchedulerBatchSize { get; set; } = 20;

    /// <summary>
    /// How many claimed items one worker runs at once. Bounded because this work talks to payment
    /// providers, and unbounded fan-out trades a latency problem for a rate-limit one.
    /// </summary>
    public int SchedulerMaxParallelism { get; set; } = 4;

    /// <summary>How long a claim holds an item before another worker may take it.</summary>
    public int SchedulerLeaseSeconds { get; set; } = 120;

    public int SchedulerMaxAttempts { get; set; } = 5;

    public int SchedulerRetryBaseSeconds { get; set; } = 30;

    public int SchedulerRetryMaxSeconds { get; set; } = 3_600;

    /// <summary>
    /// How long a completed record is kept before the TTL index removes it. Pending, processing,
    /// dead-lettered and abandoned records are never purged: money may be unfinished behind them.
    /// </summary>
    public int SchedulerCompletedRetentionDays { get; set; } = 14;

    /// <summary>
    /// How long a tenant's scheduled occurrence covers, in minutes. A producer that overlaps itself
    /// lands on one item rather than two.
    /// </summary>
    public int SchedulerBucketMinutes { get; set; } = 5;
    public int OutboxLeaseSeconds { get; set; } = 30;
    public int OutboxMaxAttempts { get; set; } = 10;
    public int CheckoutCallbackStateLifetimeMinutes { get; set; } = 60;
    public int WebhookBatchSize { get; set; } = 50;
    public int WebhookLeaseSeconds { get; set; } = 30;
    public int WebhookMaxAttempts { get; set; } = 10;
    public int WebhookIntakeTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Replay window for Stripe webhook timestamps. Defaults to Stripe's own 5 minutes;
    /// clamped so it can never be widened past an hour or disabled.
    /// </summary>
    public int StripeSignatureToleranceSeconds { get; set; } = 300;
    public int MaximumWebhookBodyBytes { get; set; } = 262_144;
    public int MaximumReturnParameterLength { get; set; } = 8_192;
    public int ReturnRequestsPerClientPerMinute { get; set; } = 60;
    public int ReturnRequestsPerStatePerMinute { get; set; } = 12;
    public int StoredPaymentMethodListRequestsPerMinute { get; set; } = 60;
    public int StoredPaymentMethodRemovalRequestsPerMinute { get; set; } = 10;
    public int PaymentQueryTenantRequestsPerMinute { get; set; } = 600;
    public int PaymentQueryActorRequestsPerMinute { get; set; } = 120;
    public int StoredPaymentMethodRemovalLeaseSeconds { get; set; } = 30;
    public int StoredPaymentMethodRemovalMaxAttempts { get; set; } = 10;
    public int MaximumRefundsPerPayment { get; set; } = 100;
    public int RefundRecoveryMaxAttempts { get; set; } = 10;
    public int MaximumCapturesPerPayment { get; set; } = 100;
    public int CaptureRecoveryMaxAttempts { get; set; } = 10;
    /// <summary>
    /// This service's own public HTTPS base, used to build the checkout return URL a provider
    /// sends the shopper back to. Derived rather than accepted from callers, because a
    /// caller-supplied return URL would let a request redirect the payment flow elsewhere.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// IAM's public HTTPS base, used to verify an organization named in a provider
    /// registration. Empty means registrations that name an organization are refused as
    /// unavailable — the same fail-closed rule the rest of this subsystem follows, because
    /// the alternative is writing configuration under an organization nobody confirmed.
    /// Registrations that name none are unaffected: they take the caller's context and never
    /// reach IAM.
    /// </summary>
    public string IamBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Whether an organization named in a registration request is checked against IAM before
    /// it is trusted. Every skipped check is logged at warning level.
    /// </summary>
    public bool VerifyOrganizationWithIam { get; set; } = true;

    /// <summary>
    /// The one organization whose callers may name a different organization in the request
    /// body. Everybody else acts as the organization their token carries, and an organization
    /// in their request is ignored.
    /// </summary>
    /// <remarks>
    /// The console runs as a single organization for every tenant and cannot switch, so
    /// configuring or simulating for any other organization is only possible if the request may
    /// say which. Applications consuming the API do carry their own organization, and for them
    /// the token is the stronger evidence, so the body is disregarded rather than trusted.
    /// <para>
    /// This is a magic value, and its safety rests on no real end user's organization being
    /// equal to it: anyone whose token carries this identifier gets the console's reach over
    /// every organization in their tenant. It is configurable so a tenant already using
    /// <c>default</c> as a genuine organization can move the console elsewhere. Setting it to
    /// empty turns the behaviour off entirely — no caller may then name an organization.
    /// </para>
    /// </remarks>
    public string ConsoleOrganizationId { get; set; } = "default";

    /// <summary>
    /// Whether a provider the console registered serves every organization in its tenant that
    /// has no configuration of its own.
    /// </summary>
    /// <remarks>
    /// A tenant configures one merchant account and its organizations buy through it, but a
    /// configuration registered from the console is stored under
    /// <see cref="ConsoleOrganizationId"/> — a real identifier, not the tenant-level null that
    /// provider resolution already falls back to. Without this, every organization but the
    /// console resolves nothing and every operation reports the provider unavailable, which is
    /// not a permission the tenant ever intended to withhold.
    /// <para>
    /// It widens resolution only. Which configuration encrypted a credential is still decided by
    /// the row that is found, so nothing moves between key rings and no stored data changes
    /// meaning. What it costs is the ability to keep a provider for the console alone: set this
    /// to <c>false</c> for a tenant that registers a console-only account — a platform-owned
    /// test merchant, say — and wants its own organizations kept off it.
    /// </para>
    /// </remarks>
    public bool TreatConsoleOrganizationAsTenantWide { get; set; } = true;

    /// <summary>
    /// How long a scope's encryption key ring is held before it is re-read from the vault. A
    /// rotated ring is not picked up by a running process until this elapses, so it trades
    /// vault traffic against rotation latency the same way <see cref="ProviderCacheSeconds"/>
    /// does for provider configuration.
    /// </summary>
    public int EncryptionKeyRingCacheSeconds { get; set; } = 300;

    /// <summary>
    /// How long a failed key ring read is remembered. Short, so a ring provisioned a moment ago
    /// is picked up quickly, but not zero — otherwise a missing secret turns every payment into
    /// a vault round trip.
    /// </summary>
    public int EncryptionKeyRingFailureCacheSeconds { get; set; } = 30;

    /// <summary>
    /// Grace period before an evicted key ring is disposed. Disposal zeroes the key bytes, and
    /// a caller that fetched the ring moments earlier may still be using it.
    /// </summary>
    public int EncryptionKeyRingDisposalGraceSeconds { get; set; } = 60;

    /// <summary>
    /// Lets a scope with no key ring of its own use the pre-migration shared ring.
    /// </summary>
    /// <remarks>
    /// On during the migration, so deploying scoped rings does not break tenants whose rings
    /// have not been provisioned yet. Switch it off once every scope has its own ring and the
    /// re-encryption job has run: while it is on, an unprovisioned scope keeps working and
    /// nothing forces the isolation this exists to achieve.
    /// </remarks>
    public bool FallBackToSharedEncryptionKeyRing { get; set; } = true;

    /// <summary>
    /// Whether provider registration creates the scope's key ring when it has none, instead
    /// of failing and waiting for an operator to run the provisioning script.
    /// </summary>
    /// <remarks>
    /// Creating a ring that does not exist cannot destroy anything, which is why this is
    /// allowed at all; the service still never modifies an existing ring, so rotation and key
    /// removal stay with the script. Turn this off and registration behaves exactly as it did
    /// before: a missing ring fails closed.
    /// <para>
    /// Requires <c>KeyVault__KeyVaultUrl</c> in the environment and a vault grant of
    /// <c>set</c>. Without either, provisioning reports itself unavailable — the same failure
    /// the manual path already produced — so this can be enabled ahead of the deployment
    /// change.
    /// </para>
    /// </remarks>
    public bool AutoProvisionKeyRing { get; set; } = true;

    /// <summary>
    /// One-shot move of vault-backed provider credentials onto their documents, encrypted.
    /// Off by default, idempotent, and safe to leave on — already-migrated providers are
    /// skipped — but intended to be switched off once every environment has run it.
    /// </summary>
    public bool MigrateProviderSecretsOnStartup { get; set; }

    public string[] TenantIds { get; set; } = [];
    public Dictionary<string, int> CurrencyMinorUnits { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BDT"] = 2,
        ["USD"] = 2,
        ["EUR"] = 2,
        ["GBP"] = 2,
        ["CHF"] = 2,
        ["JPY"] = 0,
        ["BHD"] = 3,
        ["KWD"] = 3
    };
}
