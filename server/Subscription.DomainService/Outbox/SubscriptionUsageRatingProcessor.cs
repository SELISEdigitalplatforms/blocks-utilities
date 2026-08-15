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
/// Closes usage periods that have ended and prices their overage into a
/// <see cref="SubscriptionUsageInvoice"/> — the usage clock's own sweep, independent of the fee
/// renewal sweep the way the two schedules themselves are independent.
/// </summary>
public sealed class SubscriptionUsageRatingProcessor : ISubscriptionUsageRatingProcessor
{
    /// <summary>
    /// Guards the loop that closes every period a long-outaged sweep missed, not just the most
    /// recent one. Bounded the same defensive way <c>BillingPeriodCalculator</c> bounds its own
    /// index correction — a subscription this far behind means something else is wrong.
    /// </summary>
    private const int MaximumPeriodsPerSweep = 24;

    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly ISubscriptionUsageInvoiceRepository _usageInvoices;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionUsageRatingProcessor> _logger;
    private readonly TimeProvider _time;

    public SubscriptionUsageRatingProcessor(
        ISubscriptionRepository subscriptions,
        ISubscriptionUsageRepository usage,
        ISubscriptionUsageInvoiceRepository usageInvoices,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionUsageRatingProcessor> logger,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _usage = usage;
        _usageInvoices = usageInvoices;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> CloseDuePeriodsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _subscriptions.ListDueForUsageRatingAsync(
            tenantId,
            now,
            Math.Max(1, options.UsageRatingBatchSize),
            cancellationToken);

        var closed = 0;

        foreach (var subscription in due)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantHash"] = PaymentLogValue.Hash(tenantId),
                ["SubscriptionHash"] = PaymentLogValue.Hash(subscription.ItemId)
            });

            closed += await CloseSubscriptionAsync(subscription, now, cancellationToken);
        }

        return closed;
    }

    private async Task<int> CloseSubscriptionAsync(
        SubscriptionDetail subscription,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var periodsClosed = 0;

        for (var iteration = 0; iteration < MaximumPeriodsPerSweep; iteration++)
        {
            if (subscription.CurrentUsagePeriodEndUtc > now)
            {
                break;
            }

            var periodKey = PeriodKey.Create(
                BillingInterval.Month,
                subscription.CurrentUsagePeriodStartUtc);

            await EnsureInvoiceAsync(subscription, periodKey, cancellationToken);

            if (!BillingPeriodCalculator.TryGetPeriod(
                    subscription.UsageSchedule,
                    subscription.CurrentUsagePeriodEndUtc,
                    out var nextPeriod))
            {
                _logger.LogError(
                    "A usage period could not be advanced; the schedule's time zone may no " +
                    "longer be valid");

                break;
            }

            var applied = await _subscriptions.TryTransitionAsync(
                subscription.TenantId,
                subscription.ItemId,
                new SubscriptionTransition(subscription.Status, subscription.Status)
                {
                    CurrentUsagePeriodStartUtc = nextPeriod.StartUtc,
                    CurrentUsagePeriodEndUtc = nextPeriod.EndUtc,
                    NextUsageBillingAtUtc = nextPeriod.EndUtc
                },
                cancellationToken);

            if (!applied)
            {
                // Another worker is already advancing this subscription's usage clock.
                break;
            }

            subscription.CurrentUsagePeriodStartUtc = nextPeriod.StartUtc;
            subscription.CurrentUsagePeriodEndUtc = nextPeriod.EndUtc;
            subscription.NextUsageBillingAtUtc = nextPeriod.EndUtc;
            periodsClosed++;
        }

        return periodsClosed;
    }

    /// <summary>
    /// Prices the closed period's overage and records it, unless a crash already left one behind
    /// — <c>TryCreateAsync</c>'s uniqueness index is what makes this safe to call twice.
    /// </summary>
    private async Task EnsureInvoiceAsync(
        SubscriptionDetail subscription,
        string periodKey,
        CancellationToken cancellationToken)
    {
        var existing = await _usageInvoices.GetAsync(
            subscription.TenantId,
            subscription.ItemId,
            periodKey,
            cancellationToken);

        if (existing is not null)
        {
            return;
        }

        var counters = await _usage.ListCountersAsync(
            subscription.TenantId,
            subscription.ItemId,
            periodKey,
            cancellationToken);

        var lines = new List<UsageInvoiceLine>();

        foreach (var counter in counters)
        {
            var meter = subscription.Plan.Meters.Find(
                planMeter => string.Equals(
                    planMeter.MeterKey,
                    counter.MeterKey,
                    StringComparison.Ordinal));

            // The meter was removed from the plan after this usage was recorded — nothing left
            // to rate it against. Not expected in practice; skipped rather than blocking every
            // other meter's charge.
            if (meter is null)
            {
                continue;
            }

            var amount = SubscriptionUsageRater.OverageAmountMinor(
                meter,
                counter.Balance,
                subscription.CurrencyCode);

            if (amount <= 0)
            {
                continue;
            }

            lines.Add(new UsageInvoiceLine
            {
                MeterKey = counter.MeterKey,
                OverageQuantity = Math.Max(0, counter.Balance - meter.IncludedQuantity),
                AmountMinor = amount
            });
        }

        var total = lines.Sum(line => line.AmountMinor);
        var now = _time.GetUtcNow().UtcDateTime;

        await _usageInvoices.TryCreateAsync(
            new SubscriptionUsageInvoice
            {
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                PeriodKey = periodKey,
                CurrencyCode = subscription.CurrencyCode,
                TotalAmountMinor = total,
                Lines = lines,
                State = total > 0
                    ? SubscriptionUsageInvoiceState.Pending
                    : SubscriptionUsageInvoiceState.NoCharge,
                NextAttemptAtUtc = total > 0 ? now : null,
                CorrelationId = subscription.CorrelationId
            },
            cancellationToken);
    }
}
