using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// Sends the events that were written alongside a state change.
/// </summary>
/// <remarks>
/// The two-step — append with the change, publish afterwards — exists because MongoDB and the
/// message bus share no transaction. Publishing inline would drop events exactly when something
/// went wrong; this way the event is already durable and the worst case is that it is sent late
/// or twice, both of which a consumer can handle.
/// </remarks>
public sealed class SubscriptionOutboxProcessor : ISubscriptionOutboxProcessor
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IMessageClient _messageClient;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionOutboxProcessor> _logger;
    private readonly TimeProvider _time;

    public SubscriptionOutboxProcessor(
        ISubscriptionRepository subscriptions,
        IMessageClient messageClient,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionOutboxProcessor> logger,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _messageClient = messageClient;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> PublishDueAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var subscriptions = await _subscriptions.ListWithDueEventsAsync(
            tenantId,
            now,
            Math.Max(1, options.OutboxBatchSize),
            cancellationToken);

        var published = 0;

        foreach (var subscription in subscriptions)
        {
            foreach (var pending in DueEventsOf(subscription, now))
            {
                if (await PublishAsync(subscription, pending, options, now, cancellationToken))
                {
                    published++;
                }
            }
        }

        return published;
    }

    private static IEnumerable<SubscriptionOutboxEvent> DueEventsOf(
        SubscriptionDetail subscription,
        DateTime now) =>
        subscription.OutboxEvents.Where(outboxEvent =>
            outboxEvent.Status is not SubscriptionOutboxStatus.Published
                and not SubscriptionOutboxStatus.Abandoned &&
            (outboxEvent.NextAttemptAtUtc is null ||
             outboxEvent.NextAttemptAtUtc <= now));

    private async Task<bool> PublishAsync(
        SubscriptionDetail subscription,
        SubscriptionOutboxEvent pending,
        SubscriptionOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid().ToString("N");

        var claimed = await _subscriptions.TryClaimEventAsync(
            subscription.TenantId,
            subscription.ItemId,
            pending.EventId,
            leaseId,
            now.AddSeconds(Math.Max(1, options.OutboxLeaseSeconds)),
            cancellationToken);

        if (claimed is null)
        {
            // Another worker holds it. Publishing anyway is what would make a duplicate.
            return false;
        }

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantHash"] = PaymentLogValue.Hash(subscription.TenantId),
            ["SubscriptionHash"] = PaymentLogValue.Hash(subscription.ItemId),
            ["EventType"] = PaymentLogValue.Label(claimed.EventType),
            ["CorrelationId"] = claimed.CorrelationId
        });

        try
        {
            await _messageClient.SendToMassConsumerAsync(
                new ConsumerMessage<string>
                {
                    ConsumerName = SubscriptionConstants.LifecycleTopic,
                    Payload = claimed.Payload
                });

            await _subscriptions.MarkEventPublishedAsync(
                subscription.TenantId,
                subscription.ItemId,
                claimed.EventId,
                leaseId,
                now,
                cancellationToken);

            _logger.LogInformation("Subscription event published");

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(
                subscription,
                claimed,
                leaseId,
                options,
                now,
                exception,
                cancellationToken);

            return false;
        }
    }

    private async Task RecordFailureAsync(
        SubscriptionDetail subscription,
        SubscriptionOutboxEvent claimed,
        string leaseId,
        SubscriptionOptions options,
        DateTime now,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var attempt = claimed.AttemptCount + 1;
        var exhausted = attempt >= Math.Max(1, options.OutboxMaxAttempts);

        // Backs off exponentially, capped: a broker that is down stays down for minutes, and
        // hammering it makes recovery slower for everyone.
        var delay = TimeSpan.FromSeconds(
            Math.Min(300, Math.Pow(2, Math.Min(attempt, 8))));

        await _subscriptions.MarkEventFailedAsync(
            subscription.TenantId,
            subscription.ItemId,
            claimed.EventId,
            leaseId,
            exhausted
                ? SubscriptionOutboxStatus.Abandoned
                : SubscriptionOutboxStatus.RetryScheduled,
            attempt,
            now.Add(delay),
            exception.Message,
            cancellationToken);

        _logger.Log(
            exhausted ? LogLevel.Error : LogLevel.Warning,
            "Subscription event publication failed Attempt={Attempt} Exhausted={Exhausted}",
            attempt,
            exhausted);
    }
}
