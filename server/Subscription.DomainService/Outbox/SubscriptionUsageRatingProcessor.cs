using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
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
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionUsageRatingProcessor> _logger;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly TimeProvider _time;
    private readonly ISubscriptionAuditTrail? _audit;

    public SubscriptionUsageRatingProcessor(
        ISubscriptionRepository subscriptions,
        ISubscriptionUsageRepository usage,
        ISubscriptionUsageInvoiceRepository usageInvoices,
        IBillingAccountRepository billingAccounts,
        ISubscriptionBillingGateway gateway,
        ISubscriptionOutboxEventFactory events,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionUsageRatingProcessor> logger,
        TimeProvider? time = null,
        ISubscriptionAuditTrail? audit = null,
        ISubscriptionWorkScheduler? scheduler = null)
    {
        _scheduler = scheduler;
        _subscriptions = subscriptions;
        _usage = usage;
        _usageInvoices = usageInvoices;
        _billingAccounts = billingAccounts;
        _gateway = gateway;
        _events = events;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _audit = audit;
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

            await AuditAsync(subscription, "RatingStarted", "InProgress", cancellationToken);
            var subscriptionPeriodsClosed = await CloseSubscriptionAsync(
                subscription, now, cancellationToken);
            closed += subscriptionPeriodsClosed;
            await AuditAsync(subscription, "PeriodsClosed",
                subscriptionPeriodsClosed > 0 ? "Succeeded" : "NoOp", cancellationToken);
        }

        return closed;
    }

    public Task<int> CloseSubscriptionPeriodsAsync(
        SubscriptionDetail subscription,
        DateTime asOfUtc,
        CancellationToken cancellationToken) =>
        CloseSubscriptionAsync(subscription, asOfUtc, cancellationToken);

    public Task ChargeInvoiceAsync(
        SubscriptionUsageInvoice invoice,
        CancellationToken cancellationToken) =>
        ChargeInvoiceAsync(invoice, _options.CurrentValue, _time.GetUtcNow().UtcDateTime, cancellationToken);

    private Task AuditAsync(
        SubscriptionDetail subscription,
        string stage,
        string outcome,
        CancellationToken cancellationToken) =>
        _audit is null ? Task.CompletedTask : _audit.RecordAsync(new SubscriptionAuditEvent
        {
            TenantId = subscription.TenantId,
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.ItemId,
            OperationId = $"usage-rating:{subscription.ItemId}:{subscription.CurrentUsagePeriodEndUtc:O}",
            CorrelationId = subscription.CorrelationId,
            Operation = "UsageRating",
            Stage = stage,
            Outcome = outcome,
            Source = "Worker",
            CurrencyCode = subscription.CurrencyCode
        }, cancellationToken);

    private async Task<int> CloseSubscriptionAsync(
        SubscriptionDetail subscription,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var periodsClosed = 0;

        // A plan change detaches its outgoing window atomically with the schedule swap. Rate
        // those snapshots first: their counters are still addressed by the old period key and
        // their price/allowance must come from the old plan, not the newly installed one.
        foreach (var pending in subscription.PendingUsagePeriods.ToList())
        {
            var ratingSubscription = new SubscriptionDetail
            {
                ItemId = subscription.ItemId,
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                BillingAccountId = subscription.BillingAccountId,
                Plan = pending.Plan,
                Price = pending.Price,
                CurrencyCode = pending.CurrencyCode,
                CorrelationId = pending.CorrelationId
            };

            await EnsureInvoiceAsync(ratingSubscription, pending.PeriodKey, cancellationToken);

            // The invoice exists now, so the charge is due now. Announced rather than left for the
            // next sweep: waiting only delays revenue and the subscriber's own record of what they
            // used. Best effort inside the scheduler — the invoice is already written.
            if (_scheduler is not null)
            {
                await _scheduler.ScheduleUsageInvoiceChargeAsync(
                    ratingSubscription,
                    pending.PeriodKey,
                    pending.CorrelationId,
                    cancellationToken);
            }

            await _subscriptions.TryRemovePendingUsagePeriodAsync(
                subscription.TenantId,
                subscription.ItemId,
                pending.PeriodKey,
                cancellationToken);
            periodsClosed++;
        }

        for (var iteration = 0; iteration < MaximumPeriodsPerSweep; iteration++)
        {
            if (subscription.CurrentUsagePeriodEndUtc > now)
            {
                break;
            }

            var periodKey = PeriodKey.Create(
                subscription.UsageSchedule.Interval,
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
                    StringComparison.Ordinal) &&
                    planMeter.ResetPolicy == MeterResetPolicy.Periodic);

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

        // Tax is on the aggregate, not per meter — the same "one charge, not one per meter"
        // scope this invoice already keeps for the charge itself.
        var subtotal = lines.Sum(line => line.AmountMinor);
        var tax = SubscriptionAmountCalculator.TaxAmountMinor(subtotal, subscription.Price.TaxRateBasisPoints);
        var total = subtotal + tax;
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
                TaxAmountMinor = tax,
                Lines = lines,
                State = total > 0
                    ? SubscriptionUsageInvoiceState.Pending
                    : SubscriptionUsageInvoiceState.NoCharge,
                NextAttemptAtUtc = total > 0 ? now : null,
                CorrelationId = subscription.CorrelationId
            },
            cancellationToken);
    }

    public async Task<int> ChargeDueInvoicesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _usageInvoices.ListDueAsync(
            tenantId,
            now,
            Math.Max(1, options.UsageRatingBatchSize),
            cancellationToken);

        foreach (var invoice in due)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantHash"] = PaymentLogValue.Hash(tenantId),
                ["SubscriptionHash"] = PaymentLogValue.Hash(invoice.SubscriptionId)
            });

            await ChargeInvoiceAsync(invoice, options, now, cancellationToken);
        }

        return due.Count;
    }

    private async Task ChargeInvoiceAsync(
        SubscriptionUsageInvoice invoice,
        SubscriptionOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetByIdAsync(
            invoice.TenantId,
            invoice.SubscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            // The subscription is gone; there is nothing left this invoice could be applied to.
            await _usageInvoices.TryMarkAbandonedAsync(invoice.TenantId, invoice.ItemId, cancellationToken);

            return;
        }

        var account = await _billingAccounts.GetAsync(
            invoice.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        var attemptNumber = invoice.AttemptCount + 1;

        if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
        {
            await FailAttemptAsync(
                subscription, invoice, attemptNumber, options, now, "no_payment_method", cancellationToken);

            return;
        }

        var outcome = await _gateway.ChargeAsync(
            new SubscriptionChargeRequest
            {
                TenantId = invoice.TenantId,
                // The merchant's scope, not the subscriber's — see BillingAccount.
                OrganizationId =
                    account.ProviderOrganizationId ?? invoice.OrganizationId,
                SubscriberOrganizationId = invoice.OrganizationId,
                ProviderName = account.ProviderName,
                StoredPaymentMethodId = account.DefaultPaymentMethodId,
                ProviderCustomerId = account.ProviderCustomerId,
                AmountMinor = invoice.TotalAmountMinor,
                CurrencyCode = invoice.CurrencyCode,
                OrderId = SubscriptionConstants.UsageInvoiceOrderIdFor(
                    invoice.SubscriptionId,
                    invoice.PeriodKey),
                Description = "Metered usage overage"
            },
            SubscriptionConstants.UsageInvoiceKeyFor(invoice.SubscriptionId, invoice.PeriodKey, attemptNumber),
            invoice.CorrelationId,
            cancellationToken);

        if (!outcome.IsSuccess)
        {
            _logger.LogWarning(
                "Usage invoice charge declined AttemptNumber={AttemptNumber} Reason={Reason}",
                attemptNumber,
                PaymentLogValue.Label(outcome.ErrorCode ?? "unknown"));

            await FailAttemptAsync(
                subscription, invoice, attemptNumber, options, now,
                outcome.ErrorCode ?? "unknown", cancellationToken);

            return;
        }

        if (!await _usageInvoices.TryMarkChargedAsync(
                invoice.TenantId, invoice.ItemId, outcome.Value!, cancellationToken))
        {
            // Another worker already settled this invoice. Its outcome stands.
            return;
        }

        await _subscriptions.TryAppendEventAsync(
            subscription.TenantId,
            subscription.ItemId,
            _events.CreateUsageRatingOutcome(
                subscription, SubscriptionConstants.UsageRated, invoice.PeriodKey, invoice.CorrelationId),
            cancellationToken);

        _logger.LogInformation(
            "Usage invoice charged AttemptNumber={AttemptNumber} PeriodKey={PeriodKey}",
            attemptNumber,
            PaymentLogValue.Label(invoice.PeriodKey));
    }

    /// <summary>
    /// Retries a declined or unpayable invoice up to the attempt ceiling, then abandons it —
    /// never touching the subscription's own status, since this is deliberately a second,
    /// independent invoice from the fee renewal.
    /// </summary>
    private async Task FailAttemptAsync(
        SubscriptionDetail subscription,
        SubscriptionUsageInvoice invoice,
        int attemptNumber,
        SubscriptionOptions options,
        DateTime now,
        string reason,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, options.UsageRatingMaxAttempts);

        if (attemptNumber < maxAttempts)
        {
            await _usageInvoices.RescheduleAsync(
                invoice.TenantId,
                invoice.ItemId,
                attemptNumber,
                now.AddHours(Math.Max(1, options.UsageRatingRetryHours)),
                reason,
                cancellationToken);

            return;
        }

        if (!await _usageInvoices.TryMarkAbandonedAsync(
                invoice.TenantId, invoice.ItemId, cancellationToken))
        {
            return;
        }

        await _subscriptions.TryAppendEventAsync(
            subscription.TenantId,
            subscription.ItemId,
            _events.CreateUsageRatingOutcome(
                subscription, SubscriptionConstants.UsageRatingFailed, invoice.PeriodKey, invoice.CorrelationId),
            cancellationToken);

        _logger.LogWarning(
            "Usage invoice abandoned after every retry PeriodKey={PeriodKey} Reason={Reason}",
            PaymentLogValue.Label(invoice.PeriodKey),
            PaymentLogValue.Label(reason));
    }
}
