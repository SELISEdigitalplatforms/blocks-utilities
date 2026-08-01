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
