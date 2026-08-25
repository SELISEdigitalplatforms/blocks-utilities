using Microsoft.Extensions.Logging;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Scheduling;

namespace Subscription.DomainService.Services;

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
    private readonly ILogger<SubscriptionFinancialDocumentAnnouncer> _logger;
    private readonly TimeProvider _time;

    public SubscriptionFinancialDocumentAnnouncer(
        ISubscriptionWorkScheduler scheduler,
        ILogger<SubscriptionFinancialDocumentAnnouncer> logger,
        TimeProvider? time = null)
    {
        _scheduler = scheduler;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public Task AnnouncePaymentAsync(
        SubscriptionDetail subscription,
        string paymentDetailId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return string.IsNullOrWhiteSpace(paymentDetailId)
            ? Task.CompletedTask
            : AnnounceAsync(
                subscription,
                $"{PaymentWorkKeyPrefix}{paymentDetailId}",
                paymentDetailId,
                correlationId,
                cancellationToken);
    }

    public Task AnnounceSubscriptionAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return AnnounceAsync(
            subscription,
            $"{SubscriptionWorkKeyPrefix}{subscription.ItemId}",
            subscription.ItemId,
            correlationId,
            cancellationToken);
    }

    private async Task AnnounceAsync(
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
