using System.Text.Json;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// Builds the events appended alongside a state change.
/// </summary>
/// <remarks>
/// The deduplication key is what makes appending idempotent: a transition retried after a
/// partial failure adds the event once, so a subscriber cannot be told twice that the same
/// thing happened.
/// </remarks>
public sealed class SubscriptionOutboxEventFactory : ISubscriptionOutboxEventFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SubscriptionOutboxEvent Create(
        SubscriptionDetail subscription,
        string eventType,
        string correlationId,
        string? causationId = null)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return Build(
            subscription,
            eventType,
            $"{subscription.ItemId}:{eventType}",
            NewPayload(subscription, eventType),
            correlationId,
            causationId);
    }

    public SubscriptionOutboxEvent CreateUsageThreshold(
        SubscriptionDetail subscription,
        SubscriptionUsageCounter counter,
        int thresholdPercent,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(counter);

        var payload = NewPayload(
            subscription,
            Utilities.SubscriptionConstants.UsageThresholdReached);

        payload.MeterKey = counter.MeterKey;
        payload.ThresholdPercent = thresholdPercent;
        payload.Balance = counter.Balance;
        payload.Limit = counter.LimitSnapshot;

        return Build(
            subscription,
            Utilities.SubscriptionConstants.UsageThresholdReached,
            // Scoped to the period and threshold: crossing 80% in September is a different
            // event from crossing it in August, and both must be told.
            $"{subscription.ItemId}:usage:{counter.MeterKey}:{counter.PeriodKey}:{thresholdPercent}",
            payload,
            correlationId,
            null);
    }

    private static SubscriptionOutboxEvent Build(
        SubscriptionDetail subscription,
        string eventType,
        string deduplicationKey,
        SubscriptionLifecycleEvent payload,
        string correlationId,
        string? causationId)
    {
        var eventId = Guid.NewGuid().ToString();
        payload.EventId = eventId;

        return new SubscriptionOutboxEvent
        {
            EventId = eventId,
            EventType = eventType,
            DeduplicationKey = deduplicationKey,
            Payload = JsonSerializer.Serialize(payload, SerializerOptions),
            CorrelationId = correlationId,
            CausationId = causationId
        };
    }

    private static SubscriptionLifecycleEvent NewPayload(
        SubscriptionDetail subscription,
        string eventType) => new()
    {
        EventType = eventType,
        TenantId = subscription.TenantId,
        OrganizationId = subscription.OrganizationId,
        SubscriptionId = subscription.ItemId,
        PlanCode = subscription.Plan.Code,
        Status = subscription.Status.ToString(),
        OccurredAtUtc = DateTime.UtcNow
    };
}
