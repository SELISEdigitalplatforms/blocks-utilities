using Microsoft.Extensions.Logging;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;

namespace Subscription.DomainService.Services;

/// <summary>
/// Notices when consumption crosses a threshold, once.
/// </summary>
/// <remarks>
/// Edge-triggered at the moment of the increment, not by a job that scans counters. A scanner
/// would alert late and re-alert on every pass; checking here means the crossing is detected by
/// whichever call caused it.
/// <para>
/// Which call that is gets settled by the database. Every process that sees the threshold
/// crossed attempts to record it, and only the one whose update modifies a document raises the
/// event — so a customer is told once rather than once per screening for the rest of the month.
/// </para>
/// </remarks>
public sealed class UsageThresholdEvaluator : IUsageThresholdEvaluator
{
    private readonly ISubscriptionUsageRepository _usage;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly ILogger<UsageThresholdEvaluator> _logger;
    private readonly ISubscriptionWorkScheduler? _scheduler;

    public UsageThresholdEvaluator(
        ISubscriptionUsageRepository usage,
        ISubscriptionRepository subscriptions,
        ISubscriptionOutboxEventFactory events,
        ILogger<UsageThresholdEvaluator> logger,
        ISubscriptionWorkScheduler? scheduler = null)
    {
        _usage = usage;
        _subscriptions = subscriptions;
        _events = events;
        _logger = logger;
        _scheduler = scheduler;
    }

    public async Task<int> EvaluateAsync(
        SubscriptionDetail subscription,
        SubscriptionUsageCounter counter,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(counter);

        var meter = subscription.Plan.Meters.Find(candidate =>
            string.Equals(candidate.MeterKey, counter.MeterKey, StringComparison.Ordinal));

        if (meter is null ||
            meter.ThresholdPercents.Count == 0 ||
            counter.LimitSnapshot is not { } limit ||
            limit <= 0)
        {
            return 0;
        }

        var reported = 0;

        foreach (var threshold in Crossed(meter.ThresholdPercents, counter.Balance, limit))
        {
            if (!await _usage.TryMarkThresholdNotifiedAsync(
                    counter.TenantId,
                    counter.ItemId,
                    threshold,
                    cancellationToken))
            {
                // Someone else reported it. Raising it anyway is what would duplicate the alert.
                continue;
            }

            var outboxEvent = _events.CreateUsageThreshold(
                subscription,
                counter,
                threshold,
                correlationId);

            var appended = await _subscriptions.TryAppendEventAsync(
                subscription.TenantId,
                subscription.ItemId,
                outboxEvent,
                cancellationToken);

            // The aggregate write is authoritative. Announce it immediately when possible; a
            // failed root-db write is healed by the repair sweep and must not fail usage recording.
            if (appended && _scheduler is not null)
            {
                await _scheduler.ScheduleOutboxPublicationAsync(
                    subscription,
                    outboxEvent,
                    cancellationToken);
            }

            _logger.LogInformation(
                "Usage threshold reached TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
                "Meter={Meter} ThresholdPercent={ThresholdPercent} Balance={Balance} " +
                "Included={Included} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(counter.MeterKey),
                threshold,
                counter.Balance,
                limit,
                correlationId);

            reported++;
        }

        return reported;
    }

    /// <summary>
    /// The thresholds this balance has reached, lowest first.
    /// </summary>
    /// <remarks>
    /// Ascending order matters when a single large increment jumps several at once: reporting
    /// 80 before 100 reads as a sequence to whoever receives them, rather than as two
    /// simultaneous and apparently contradictory alerts.
    /// <para>
    /// Compared by multiplication rather than by computing a percentage, so integer division
    /// cannot round a crossing away — at 79.6% nothing has been reached yet.
    /// </para>
    /// </remarks>
    private static IEnumerable<int> Crossed(
        IEnumerable<int> thresholds,
        long balance,
        long limit) =>
        thresholds
            .Where(threshold => balance * 100 >= limit * (long)threshold)
            .Order();
}
