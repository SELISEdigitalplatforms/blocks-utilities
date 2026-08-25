using Microsoft.Extensions.Logging;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;

namespace Subscription.DomainService.Services;

/// <summary>
/// Records that a financial event owes a document, then asks for it to be written.
/// </summary>
/// <remarks>
/// Two steps in that order, and the order is the point. The record is a write to the subscription's
/// own database and is what makes the obligation durable and its terms historical; the schedule is a
/// write to the work queue and is only what makes it prompt. Losing the second costs a delay the sweep
/// closes. Losing the first used to cost a document, which is why it no longer happens second.
/// </remarks>
public sealed class SubscriptionFinancialDocumentAnnouncer :
    ISubscriptionFinancialDocumentAnnouncer
{
    /// <summary>
    /// The work-key prefixes that say what an announcement is about.
    /// </summary>
    /// <remarks>
    /// Shared with the handler through these constants rather than spelled at each end, for the
    /// reason the settlement order-id segments are: two spellings of one label is how a plan-change
    /// invoice ended up classified as a renewal.
    /// </remarks>
    public const string PaymentWorkKeyPrefix = "payment:";

    public const string SubscriptionWorkKeyPrefix = "subscription:";

    private readonly ISubscriptionWorkScheduler _scheduler;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ILogger<SubscriptionFinancialDocumentAnnouncer> _logger;
    private readonly TimeProvider _time;

    public SubscriptionFinancialDocumentAnnouncer(
        ISubscriptionWorkScheduler scheduler,
        ISubscriptionRepository subscriptions,
        ILogger<SubscriptionFinancialDocumentAnnouncer> logger,
        TimeProvider? time = null)
    {
        _scheduler = scheduler;
        _subscriptions = subscriptions;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task AnnounceChargeAsync(
        SubscriptionDetail subscription,
        string paymentDetailId,
        SubscriptionChargeKind chargeKind,
        string? periodKey,
        string correlationId,
        CancellationToken cancellationToken,
        FinancialDocumentPerson? initiatedBy = null)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (string.IsNullOrWhiteSpace(paymentDetailId))
        {
            return;
        }

        await RecordAsync(
            subscription,
            SubscriptionDocumentSourceFactory.ForCharge(
                subscription,
                paymentDetailId,
                chargeKind,
                periodKey,
                initiatedBy,
                _time.GetUtcNow().UtcDateTime,
                correlationId),
            cancellationToken);

        await ScheduleAsync(
            subscription,
            $"{PaymentWorkKeyPrefix}{paymentDetailId}",
            paymentDetailId,
            correlationId,
            cancellationToken);
    }

    public async Task AnnounceTrialAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken,
        FinancialDocumentPerson? initiatedBy = null)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (SubscriptionDocumentSourceFactory.ForTrial(subscription, initiatedBy, correlationId)
            is { } source)
        {
            await RecordAsync(subscription, source, cancellationToken);
        }

        // Keyed on the subscription rather than on the trial: the handler drains whatever that
        // subscription owes, so one key covers a trial and anything else recorded beside it.
        await ScheduleAsync(
            subscription,
            $"{SubscriptionWorkKeyPrefix}{subscription.ItemId}",
            subscription.ItemId,
            correlationId,
            cancellationToken);
    }

    public Task RequestPendingAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return ScheduleAsync(
            subscription,
            $"{SubscriptionWorkKeyPrefix}{subscription.ItemId}",
            subscription.ItemId,
            correlationId,
            cancellationToken);
    }

    /// <summary>
    /// Appends the obligation, and treats an existing one as success.
    /// </summary>
    /// <remarks>
    /// A retried money path announces the same event twice, and the append is filtered on the key so
    /// the second changes nothing. That is the expected outcome, not a conflict.
    /// </remarks>
    private async Task RecordAsync(
        SubscriptionDetail subscription,
        SubscriptionDocumentSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            await _subscriptions.TryAppendDocumentSourceAsync(
                subscription.TenantId,
                subscription.ItemId,
                source,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An error rather than a warning. The document can still be recovered from the payment,
            // but it will then be composed from the subscription as it stands rather than from the
            // terms this would have frozen — so a later plan change can make it describe the wrong
            // plan. That is worth an operator knowing about.
            _logger.LogError(
                exception,
                "A settled subscription event could not record what its document is for; the " +
                "document will be recovered from the payment instead " +
                "SubscriptionHash={SubscriptionHash} DocumentType={DocumentType}",
                PaymentLogValue.Hash(subscription.ItemId),
                source.DocumentType);
        }
    }

    private async Task ScheduleAsync(
        SubscriptionDetail subscription,
        string workKey,
        string aggregateId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _scheduler.TryScheduleAsync(
                SubscriptionWorkType.FinancialDocumentIssue,
                subscription.TenantId,
                workKey,
                _time.GetUtcNow().UtcDateTime,
                correlationId,
                aggregateId,
                subscription.OrganizationId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "A settled subscription event could not have its document scheduled; the repair " +
                "sweep will pick it up SubscriptionHash={SubscriptionHash}",
                PaymentLogValue.Hash(subscription.ItemId));
        }
    }
}
