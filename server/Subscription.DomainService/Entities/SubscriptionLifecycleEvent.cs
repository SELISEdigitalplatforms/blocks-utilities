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

    public string Status { get; set; } = string.Empty;

    /// <summary>Set on usage events; absent on lifecycle transitions.</summary>
    public string? MeterKey { get; set; }

    public long? ThresholdPercent { get; set; }

    public long? Balance { get; set; }

    public long? Limit { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
}
