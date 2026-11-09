using System.Text.Json;
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
/// Sends the events written onto an organization's credit grants (spec 005).
/// </summary>
/// <remarks>
/// A purchase or free grant inserts the grant with its event already inside it, and an exhaustion
/// appends one under a deduplication key, so the fact and its announcement are one write and a
/// retry adds nothing. From there this is the subscription outbox's loop against the grant
/// collection: lease, publish, mark; back off and eventually abandon on failure. Payloads of the
/// events that become mail are cleared once published, as the subscription outbox clears its own,
/// because they name the buyer's billing account.
/// </remarks>
public sealed class OrganizationCreditOutboxProcessor : IOrganizationCreditOutboxProcessor
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ISubscriptionCreditGrantRepository _grants;
    private readonly IMessageClient _messageClient;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<OrganizationCreditOutboxProcessor> _logger;
    private readonly TimeProvider _time;

    public OrganizationCreditOutboxProcessor(
        ISubscriptionCreditGrantRepository grants,
        IMessageClient messageClient,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<OrganizationCreditOutboxProcessor> logger,
        TimeProvider? time = null)
    {
        _grants = grants;
        _messageClient = messageClient;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> PublishDueAsync(string tenantId, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var grants = await _grants.ListWithDueEventsAsync(
            tenantId, now, Math.Max(1, _options.CurrentValue.OutboxBatchSize), cancellationToken);

        var published = 0;

        foreach (var grant in grants)
        {
            published += await PublishDueForGrantAsync(grant, cancellationToken);
        }

        return published;
    }

    public async Task<int> PublishDueForGrantAsync(SubscriptionCreditGrant grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;
        var published = 0;

        // Materialized first: a successful publish mutates nothing here, but a claim that comes back
        // from the store is a different object and the list must not change under the loop.
        foreach (var pending in grant.OutboxEvents.Where(outboxEvent => IsDue(outboxEvent, now)).ToList())
        {
            if (await PublishAsync(grant, pending, options, now, cancellationToken))
            {
                published++;
            }
        }

        return published;
    }

    private static bool IsDue(SubscriptionOutboxEvent outboxEvent, DateTime now) =>
        outboxEvent.Status is not SubscriptionOutboxStatus.Published and not SubscriptionOutboxStatus.Abandoned &&
        (outboxEvent.NextAttemptAtUtc is null || outboxEvent.NextAttemptAtUtc <= now);

    private async Task<bool> PublishAsync(
        SubscriptionCreditGrant grant,
        SubscriptionOutboxEvent pending,
        SubscriptionOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid().ToString("N");
        var claimed = await _grants.TryClaimEventAsync(
            grant.TenantId,
            grant.ItemId,
            pending.EventId,
            leaseId,
            now.AddSeconds(Math.Max(1, options.OutboxLeaseSeconds)),
            cancellationToken);

        if (claimed is null)
        {
            // Another worker holds it, or it was published between the read and the claim.
            return false;
        }

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = PaymentLogValue.Id(grant.TenantId),
            ["OrganizationId"] = PaymentLogValue.Id(grant.OrganizationId),
            ["CreditGrantId"] = PaymentLogValue.Id(grant.ItemId),
            ["EventType"] = PaymentLogValue.Label(claimed.EventType),
            ["CorrelationId"] = claimed.CorrelationId
        });

        try
        {
            var payload = JsonSerializer.Deserialize<SubscriptionLifecycleEvent>(claimed.Payload, ReadOptions) ??
                throw new InvalidOperationException($"Organization credit event {claimed.EventId} has an empty payload.");

            await _messageClient.SendToMassConsumerAsync(
                new ConsumerMessage<SubscriptionLifecycleEvent>
                {
                    ConsumerName = SubscriptionConstants.LifecycleTopic,
                    Payload = payload
                });

            await _grants.MarkEventPublishedAsync(
                grant.TenantId,
                grant.ItemId,
                claimed.EventId,
                leaseId,
                now,
                clearPayload: SubscriptionConstants.NotificationMailPurposes.ContainsKey(claimed.EventType),
                cancellationToken);

            // So a caller holding this grant sees what the store now says, without a re-read.
            pending.Status = SubscriptionOutboxStatus.Published;
            pending.PublishedAtUtc = now;

            _logger.LogInformation("Organization credit event published");

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var attempt = claimed.AttemptCount + 1;
            var exhausted = attempt >= Math.Max(1, options.OutboxMaxAttempts);
            // The subscription outbox's backoff, for the same reason: a broker that is down stays
            // down for minutes.
            var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(attempt, 8))));

            await _grants.MarkEventFailedAsync(
                grant.TenantId,
                grant.ItemId,
                claimed.EventId,
                leaseId,
                exhausted ? SubscriptionOutboxStatus.Abandoned : SubscriptionOutboxStatus.RetryScheduled,
                attempt,
                now.Add(delay),
                exception.Message,
                cancellationToken);

            pending.Status = exhausted ? SubscriptionOutboxStatus.Abandoned : SubscriptionOutboxStatus.RetryScheduled;
            pending.AttemptCount = attempt;
            pending.NextAttemptAtUtc = now.Add(delay);

            _logger.Log(
                exhausted ? LogLevel.Error : LogLevel.Warning,
                exception,
                "Organization credit event publication failed Attempt={Attempt} Exhausted={Exhausted}",
                attempt,
                exhausted);

            return false;
        }
    }
}
