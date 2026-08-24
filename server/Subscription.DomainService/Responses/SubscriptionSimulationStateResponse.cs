namespace Subscription.DomainService.Responses;

/// <summary>
/// A complete, read-only snapshot of one subscription for the simulation harness — everything a
/// tester needs to see in one call rather than piecing it together from several ordinary
/// endpoints.
/// </summary>
/// <remarks>
/// Deliberately narrower than the underlying documents. It never carries a stored payment
/// method id, a provider customer id, a checkout URL, or an outbox event's raw payload — the
/// same redaction the business audit trail already applies, because this is still a diagnostic
/// surface an administrator reads, not a database export.
/// </remarks>
public sealed class SubscriptionSimulationStateResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string OrganizationId { get; init; } = string.Empty;

    public SubscriptionResponse Subscription { get; init; } = new();

    /// <summary>Null when the entitlement read itself failed; the rest of the state is still returned.</summary>
    public EntitlementSnapshotResponse? Entitlements { get; init; }

    public SimulationSettlementReservationResponse? SettlementReservation { get; init; }

    public SimulationPendingCheckoutResponse? PendingCheckout { get; init; }

    public List<SimulationPaymentResponse> Payments { get; init; } = [];

    public List<SimulationUsageInvoiceResponse> UsageInvoices { get; init; } = [];

    /// <summary>Null when <c>includeBackgroundWork</c> was not requested.</summary>
    public SimulationBackgroundWorkResponse? BackgroundWork { get; init; }

    public List<SubscriptionAuditEventResponse> AuditEvents { get; init; } = [];

    public string CorrelationId { get; init; } = string.Empty;
}

/// <summary>
/// An increase reserved but not yet settled. Carries none of
/// <c>StoredPaymentMethodId</c>/<c>ProviderCustomerId</c>/<c>ProviderOrganizationId</c> — the
/// same fields the financial audit trail excludes.
/// </summary>
public sealed class SimulationSettlementReservationResponse
{
    public string ReservationId { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public long ChargeAmountMinor { get; init; }

    public DateTime ReservedAtUtc { get; init; }

    public int ReservedAtVersion { get; init; }

    public string CorrelationId { get; init; } = string.Empty;
}

/// <summary>The outstanding checkout link, if the first charge has not settled yet.</summary>
public sealed class SimulationPendingCheckoutResponse
{
    public string PaymentDetailId { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public int AttemptCount { get; init; }

    public DateTime? NextCheckAtUtc { get; init; }

    public string? LastError { get; init; }
}

/// <summary>One settled, invoiced payment — never the provider's own invoice or customer id.</summary>
public sealed class SimulationPaymentResponse
{
    public string PaymentDetailId { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public string? OrderId { get; init; }

    public string? Description { get; init; }

    public decimal Amount { get; init; }

    public decimal RefundedAmount { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime IssuedAtUtc { get; init; }
}

public sealed class SimulationUsageInvoiceResponse
{
    public string UsageInvoiceId { get; init; } = string.Empty;

    public string PeriodKey { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    public long TotalAmountMinor { get; init; }

    public long TaxAmountMinor { get; init; }

    /// <summary>What was taxed, before tax and before any credit was spent.</summary>
    public long NetAmountMinor { get; init; }

    /// <summary>Basis points on the price this was charged from. Null when untaxed.</summary>
    public int? TaxRateBasisPoints { get; init; }

    /// <summary>"Exclusive" or "Inclusive", for a harness that has to show the same figures a UI does.</summary>
    public string? TaxMode { get; init; }

    public string State { get; init; } = string.Empty;

    public int AttemptCount { get; init; }

    public DateTime? NextAttemptAtUtc { get; init; }

    public string? PaymentDetailId { get; init; }

    public string? LastError { get; init; }
}

/// <summary>
/// The subscription's own outbox, grouped by status. Never the event's raw payload — only what
/// a tester needs to tell a stuck job from a healthy one.
/// </summary>
public sealed class SimulationBackgroundWorkResponse
{
    public int PendingCount { get; init; }

    public int ProcessingCount { get; init; }

    public int RetryScheduledCount { get; init; }

    public int PublishedCount { get; init; }

    /// <summary>The dead letter: an event the outbox has given up retrying.</summary>
    public int AbandonedCount { get; init; }

    public List<SimulationBackgroundWorkItemResponse> Items { get; init; } = [];
}

public sealed class SimulationBackgroundWorkItemResponse
{
    public string EventId { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int AttemptCount { get; init; }

    public DateTime? NextAttemptAtUtc { get; init; }

    public DateTime? LeaseExpiresAtUtc { get; init; }

    public string? LastError { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }
}
