namespace Subscription.DomainService.Entities;

/// <summary>
/// What is published to the lifecycle topic when something happens to a subscription.
/// </summary>
/// <remarks>
/// The platform states the fact; each product decides what it means. A quota alert is an event
/// here rather than an email because what counts as "warn the customer" differs per product,
/// and this service has no business owning that decision — or a mail server.
/// </remarks>
public sealed class SubscriptionLifecycleEvent
{
    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = 1;

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    /// <summary>Set only on a plan-change event; <see cref="PlanCode"/> already carries the new one.</summary>
    public string? PreviousPlanCode { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Whether the subscription is running out a scheduled cancellation rather than having already
    /// ended.
    /// </summary>
    /// <remarks>
    /// Carried on every lifecycle event because <see cref="Status"/> alone cannot express it: a
    /// subscriber who cancels keeps what they paid for, so the status stays <c>Active</c> until the
    /// boundary passes. A consumer told only that a cancellation was requested, with no way to see
    /// this pair, has nothing to do but revoke on receipt — which takes access away on the day
    /// someone cancels, the very thing this module refuses to do.
    /// </remarks>
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>
    /// The instant entitlement actually stops — the paid period's end for a scheduled cancellation,
    /// the moment it ended for one that has taken effect. Null only where the event says nothing
    /// about a boundary.
    /// </summary>
    public DateTime? CurrentPeriodEndUtc { get; set; }

    /// <summary>Set on usage events; absent on lifecycle transitions.</summary>
    public string? MeterKey { get; set; }

    public long? ThresholdPercent { get; set; }

    public decimal? Balance { get; set; }

    public decimal? Limit { get; set; }

    /// <summary>Set on renewal and dunning events; absent otherwise.</summary>
    public string? PeriodKey { get; set; }

    /// <summary>The dunning attempt this event resulted from, 1-based.</summary>
    public int? AttemptNumber { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
}
