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

    public SubscriptionOutboxEvent CreateRenewalOutcome(
        SubscriptionDetail subscription,
        string eventType,
        string periodKey,
        int attemptNumber,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var payload = NewPayload(subscription, eventType);
        payload.PeriodKey = periodKey;
        payload.AttemptNumber = attemptNumber;

        return Build(
            subscription,
            eventType,
            // Scoped to the attempt: each dunning retry is a distinct outcome, not a replay of
            // the one before it, and a downstream consumer needs to tell them apart.
            $"{subscription.ItemId}:{eventType}:{periodKey}:{attemptNumber}",
            payload,
            correlationId,
            null);
    }

    /// <summary>
    /// Raised when a purchased quantity actually moves — an applied increase, or a renewal
    /// carrying out a scheduled decrease. Not raised when a decrease is merely scheduled, which
    /// changes nothing the subscriber holds yet.
    /// </summary>
    public SubscriptionOutboxEvent CreateQuantityChanged(
        SubscriptionDetail subscription,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return Build(
            subscription,
            Utilities.SubscriptionConstants.SubscriptionQuantityChanged,
            // Version is unique per mutation, so it is free scoping — a quantity change has no
            // period key or attempt number the way a renewal does.
            $"{subscription.ItemId}:{Utilities.SubscriptionConstants.SubscriptionQuantityChanged}:{subscription.Version}",
            NewPayload(subscription, Utilities.SubscriptionConstants.SubscriptionQuantityChanged),
            correlationId,
            null);
    }

    public SubscriptionOutboxEvent CreatePlanChanged(
        SubscriptionDetail subscription,
        string previousPlanCode,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var payload = NewPayload(subscription, Utilities.SubscriptionConstants.SubscriptionPlanChanged);
        payload.PreviousPlanCode = previousPlanCode;

        return Build(
            subscription,
            Utilities.SubscriptionConstants.SubscriptionPlanChanged,
            // Version is already unique per mutation, so it is free scoping — no period key or
            // attempt number applies to a plan change the way it does to a renewal.
            $"{subscription.ItemId}:{Utilities.SubscriptionConstants.SubscriptionPlanChanged}:{subscription.Version}",
            payload,
            correlationId,
            null);
    }

    public SubscriptionOutboxEvent CreateUsageRatingOutcome(
        SubscriptionDetail subscription,
        string eventType,
        string periodKey,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var payload = NewPayload(subscription, eventType);
        payload.PeriodKey = periodKey;

        return Build(
            subscription,
            eventType,
            // No attempt number: unlike a renewal's per-attempt events, this fires exactly once
            // — at the invoice's terminal outcome, whichever attempt that turns out to be.
            $"{subscription.ItemId}:{eventType}:{periodKey}",
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
