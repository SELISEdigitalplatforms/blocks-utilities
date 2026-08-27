using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// The periodic sweep that finishes a scheduled cancellation once the period it was waiting out
/// has actually ended.
/// </summary>
/// <remarks>
/// <see cref="SubscriptionCancellationService"/> only ever records the intention — a subscriber
/// who cancels keeps what they paid for, so nothing about the interactive request can be the
/// thing that stops access on a date nobody is watching a screen for. This sweep is that watcher.
/// </remarks>
public sealed class SubscriptionCancellationEffectiveProcessor : ISubscriptionCancellationEffectiveProcessor
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionCancellationEffectiveProcessor> _logger;
    private readonly TimeProvider _time;

    public SubscriptionCancellationEffectiveProcessor(
        ISubscriptionRepository subscriptions,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionCancellationEffectiveProcessor> logger,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _events = events;
        _cache = cache;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> ProcessDueAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _subscriptions.ListDueForCancellationAsync(
            tenantId,
            now,
            Math.Max(1, options.CancellationBatchSize),
            cancellationToken);

        var ended = 0;

        foreach (var subscription in due)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantHash"] = PaymentLogValue.Hash(tenantId),
                ["SubscriptionHash"] = PaymentLogValue.Hash(subscription.ItemId)
            });

            // A lost compare-and-set here means another worker — or an interactive escalation
            // request racing this very pass — already ended it. Its outcome stands either way,
            // so this is not an error; the sweep simply moves on.
            var applied = await _subscriptions.TryTransitionAsync(
                subscription.TenantId,
                subscription.ItemId,
                new SubscriptionTransition(subscription.Status, SubscriptionStatus.Canceled)
                {
                    CancelAtPeriodEnd = false,
                    CanCancelImmediately = false,
                    EndedAtUtc = now,
                    ClearNextFeeBillingAt = true,
                    ClearNextUsageBillingAt = true,
                    // The still-open final window would otherwise never be rated: the usage sweep
                    // only ever looked at live subscriptions, and this write is what takes this
                    // one out of that set. Queuing it here, atomically with the status change, is
                    // what a plan change does with its own outgoing window — captured in the same
                    // compare-and-set that would otherwise let it be forgotten.
                    OutgoingUsagePeriod = OutgoingUsagePeriodOf(subscription),
                    Event = _events.Create(
                        subscription,
                        SubscriptionConstants.SubscriptionCanceled,
                        subscription.CorrelationId)
                },
                cancellationToken);

            if (!applied)
            {
                continue;
            }

            _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);
            ended++;
        }

        return ended;
    }

    /// <summary>
    /// Freezes the subscription's current usage window exactly as a plan change freezes its own
    /// outgoing one, so the rating sweep can price it after status has already moved on.
    /// </summary>
    private static PendingUsagePeriod OutgoingUsagePeriodOf(SubscriptionDetail subscription) => new()
    {
        PeriodKey = PeriodKey.Create(
            subscription.UsageSchedule.Interval,
            subscription.CurrentUsagePeriodStartUtc),
        PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
        PeriodEndUtc = subscription.CurrentUsagePeriodEndUtc,
        Plan = subscription.Plan,
        Price = subscription.Price,
        CurrencyCode = subscription.CurrencyCode,
        CorrelationId = subscription.CorrelationId
    };
}
