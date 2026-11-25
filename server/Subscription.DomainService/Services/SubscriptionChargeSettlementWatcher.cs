using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Re-reads one applied charge's payment. Settled: done. Still pending: schedules the next check.
/// Failed: a usage invoice is reopened for its own retry cadence; anything else becomes an
/// <see cref="Entities.OutstandingSettlement"/> the dunning cycle collects.
/// </summary>
/// <remarks>
/// A pure read until the payment is terminal, so running a check twice is harmless. The subscription
/// module keeps watching payments by polling rather than consuming the payment lifecycle topic,
/// exactly as activation and reservation recovery do.
/// </remarks>
public sealed class SubscriptionChargeSettlementWatcher : ISubscriptionChargeSettlementWatcher
{
    private static readonly string[] SettledStatuses =
    [
        PaymentStatuses.Captured,
        PaymentStatuses.PartiallyCaptured,
        PaymentStatuses.Refunded,
        PaymentStatuses.PartiallyRefunded
    ];

    private static readonly string[] FailedStatuses =
    [
        PaymentStatuses.Expired,
        PaymentStatuses.Cancelled,
        PaymentStatuses.Refused,
        PaymentStatuses.MakePaymentFailed
    ];

    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPaymentRepository _payments;
    private readonly ISubscriptionUsageInvoiceRepository _usageInvoices;
    private readonly ISubscriptionRenewalService _renewals;
    private readonly ISubscriptionWorkScheduler _scheduler;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionChargeSettlementWatcher> _logger;
    private readonly TimeProvider _time;

    public SubscriptionChargeSettlementWatcher(
        ISubscriptionRepository subscriptions,
        IPaymentRepository payments,
        ISubscriptionUsageInvoiceRepository usageInvoices,
        ISubscriptionRenewalService renewals,
        ISubscriptionWorkScheduler scheduler,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionChargeSettlementWatcher> logger,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _payments = payments;
        _usageInvoices = usageInvoices;
        _renewals = renewals;
        _scheduler = scheduler;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionWorkOutcome> CheckAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (!ChargeSettlementWatchKey.TryParse(work.WorkKey, out var paymentDetailId, out var checkNumber))
        {
            return SubscriptionWorkOutcome.Permanent(
                "settlement_watch_key_invalid",
                "The work key does not name a payment.");
        }

        var subscription = await _subscriptions.GetByIdAsync(
            work.TenantId, work.AggregateId, cancellationToken);

        if (subscription is null)
        {
            return SubscriptionWorkOutcome.Permanent(
                "subscription_not_found",
                "The subscription this work names no longer exists.");
        }

        var payment = await _payments.GetByIdAsync(work.TenantId, paymentDetailId, cancellationToken);

        if (payment is null)
        {
            // Written before the provider is asked, so a missing record is a read that failed.
            return SubscriptionWorkOutcome.Retry(
                "payment_not_found",
                "The payment this charge raised could not be read.");
        }

        if (SettledStatuses.Contains(payment.PaymentStatus))
        {
            return SubscriptionWorkOutcome.Completed();
        }

        if (FailedStatuses.Contains(payment.PaymentStatus))
        {
            _logger.LogWarning(
                "An applied subscription charge failed after acceptance TenantId={TenantId} " +
                "SubscriptionId={SubscriptionId} PaymentId={PaymentId} PaymentStatus={PaymentStatus}",
                PaymentLogValue.Id(work.TenantId),
                PaymentLogValue.Id(subscription.ItemId),
                PaymentLogValue.Id(payment.ItemId),
                PaymentLogValue.Label(payment.PaymentStatus));

            var usageInvoice = (await _usageInvoices.ListBySubscriptionAsync(
                    work.TenantId, subscription.ItemId, 100, cancellationToken))
                .FirstOrDefault(invoice =>
                    invoice.State == SubscriptionUsageInvoiceState.Charged &&
                    string.Equals(invoice.PaymentDetailId, payment.ItemId, StringComparison.Ordinal));

            if (usageInvoice is not null)
            {
                // Its own cadence, never the subscription's status — see FailAttemptAsync.
                var options = _options.CurrentValue;
                var reopened = await _usageInvoices.TryReopenChargedAsync(
                    work.TenantId,
                    usageInvoice.ItemId,
                    payment.ItemId,
                    _time.GetUtcNow().UtcDateTime.AddHours(Math.Max(1, options.UsageRatingRetryHours)),
                    payment.PaymentStatus,
                    cancellationToken);

                if (reopened)
                {
                    await _scheduler.ScheduleUsageInvoiceChargeAsync(
                        subscription, usageInvoice.PeriodKey, work.CorrelationId, cancellationToken);
                }

                return SubscriptionWorkOutcome.Completed();
            }

            await _renewals.RecordLateChargeFailureAsync(subscription, payment, cancellationToken);

            return SubscriptionWorkOutcome.Completed();
        }

        if (checkNumber >= Math.Max(1, _options.CurrentValue.ChargeSettlementWatchMaxChecks))
        {
            _logger.LogError(
                "An applied subscription charge is still unsettled after every check; the watch " +
                "stops here TenantId={TenantId} SubscriptionId={SubscriptionId} PaymentId={PaymentId} " +
                "PaymentStatus={PaymentStatus}",
                PaymentLogValue.Id(work.TenantId),
                PaymentLogValue.Id(subscription.ItemId),
                PaymentLogValue.Id(payment.ItemId),
                PaymentLogValue.Label(payment.PaymentStatus));

            return SubscriptionWorkOutcome.Completed();
        }

        await _scheduler.ScheduleChargeSettlementWatchAsync(
            subscription, payment.ItemId, checkNumber + 1, work.CorrelationId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}
