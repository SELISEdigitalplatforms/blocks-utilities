using System.Diagnostics;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Outbox;

public sealed class PaymentRefundOutboxProcessor :
    IPaymentRefundOutboxProcessor
{
    private readonly IPaymentRefundRepository _refunds;
    private readonly IMessageClient _messageClient;
    private readonly IPaymentWorkDispatcher _workDispatcher;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentRefundOutboxProcessor> _logger;

    public PaymentRefundOutboxProcessor(
        IPaymentRefundRepository refunds,
        IMessageClient messageClient,
        IPaymentWorkDispatcher workDispatcher,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentRefundOutboxProcessor> logger)
    {
        _refunds = refunds;
        _messageClient = messageClient;
        _workDispatcher = workDispatcher;
        _options = options;
        _logger = logger;
    }

    public async Task<int> PublishDueAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;
        var batchSize = Math.Clamp(
            options.OutboxBatchSize,
            1,
            200);
        var tenantHash = PaymentLogValue.Hash(tenantId);
        var payments =
            await _refunds
                .GetPaymentsWithDueRefundOutboxEventsAsync(
                    tenantId,
                    now,
                    batchSize,
                    cancellationToken);
        var published = 0;

        foreach (var payment in payments)
        {
            foreach (var refund in payment.Refunds)
            {
                foreach (var outboxEvent in DueEvents(
                             refund,
                             now,
                             batchSize - published))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (published >= batchSize)
                    {
                        return published;
                    }

                    var leaseId = Guid.NewGuid().ToString("N");
                    var leaseUntil = DateTime.UtcNow.AddSeconds(
                        Math.Clamp(
                            options.OutboxLeaseSeconds,
                            10,
                            300));
                    var claimed =
                        await _refunds.TryClaimOutboxEventAsync(
                            tenantId,
                            payment.ItemId,
                            refund.RefundId,
                            outboxEvent.EventId,
                            leaseId,
                            leaseUntil,
                            cancellationToken);

                    if (!claimed)
                    {
                        continue;
                    }

                    // From the refund that was requested, so a publish long after the fact
                    // still logs under the request that asked for the refund.
                    using var correlation = PaymentCorrelation.Begin(
                        refund.CorrelationId);
                    using var scope = PaymentLogScope.Begin(
                        _logger,
                        PaymentOperations.RefundOutboxPublish,
                        tenantId,
                        payment.ItemId,
                        extra: new Dictionary<string, object?>
                        {
                            ["RefundId"] =
                                PaymentLogValue.Id(refund.RefundId),
                            ["OutboxEventId"] =
                                PaymentLogValue.Id(outboxEvent.EventId),
                            ["OutboxEventType"] =
                                PaymentLogValue.Label(
                                    outboxEvent.EventType)
                        });

                    try
                    {
                        await _messageClient
                            .SendToMassConsumerAsync(
                                new ConsumerMessage<
                                    PaymentLifecycleEvent>
                                {
                                    ConsumerName =
                                        PaymentConstants
                                            .LifecycleTopic,
                                    Payload =
                                        outboxEvent.Payload
                                });

                        await _refunds.MarkOutboxPublishedAsync(
                            tenantId,
                            payment.ItemId,
                            refund.RefundId,
                            outboxEvent.EventId,
                            leaseId,
                            DateTime.UtcNow,
                            cancellationToken);

                        published++;
                    }
                    catch (Exception exception)
                        when (exception is not
                              OperationCanceledException)
                    {
                        await ScheduleRetryAsync(
                            tenantId,
                            payment.ItemId,
                            refund.RefundId,
                            outboxEvent,
                            leaseId,
                            exception,
                            cancellationToken);
                    }
                }
            }
        }

        if (published > 0)
        {
            _logger.LogInformation(
                "Payment refund outbox scan completed TenantHash={TenantHash} PublishedCount={PublishedCount} DurationMs={DurationMs}",
                tenantHash,
                published,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        return published;
    }

    private async Task ScheduleRetryAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        PaymentOutboxEvent outboxEvent,
        string leaseId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var attempts = outboxEvent.AttemptCount + 1;
        var status = attempts >= Math.Max(
            1,
            _options.CurrentValue.OutboxMaxAttempts)
            ? PaymentOutboxStatus.DeadLettered
            : PaymentOutboxStatus.RetryScheduled;
        var delaySeconds = Math.Min(
            300,
            (int)Math.Pow(
                2,
                Math.Min(attempts, 8)) +
            Random.Shared.Next(0, 5));

        await _refunds.MarkOutboxFailedAsync(
            tenantId,
            paymentDetailId,
            refundId,
            outboxEvent.EventId,
            leaseId,
            status,
            attempts,
            DateTime.UtcNow.AddSeconds(delaySeconds),
            exception.GetType().Name,
            cancellationToken);

        if (status != PaymentOutboxStatus.DeadLettered)
        {
            await _workDispatcher.TryDispatchAsync(
                tenantId,
                includeRecovery: false,
                scheduledAtUtc:
                    new DateTimeOffset(
                        DateTime.UtcNow.AddSeconds(
                            delaySeconds),
                        TimeSpan.Zero),
                cancellationToken:
                    cancellationToken);
        }

        _logger.LogError(
            exception,
            "Payment refund outbox publish failed Attempt={Attempt} FinalStatus={Status} RetryDelaySeconds={RetryDelaySeconds}",
            attempts,
            status,
            delaySeconds);
    }

    private static IEnumerable<PaymentOutboxEvent> DueEvents(
        PaymentRefund refund,
        DateTime now,
        int limit) =>
        refund.OutboxEvents
            .Where(outboxEvent =>
                outboxEvent.NextAttemptAtUtc <= now &&
                (outboxEvent.Status is
                    PaymentOutboxStatus.Pending or
                    PaymentOutboxStatus.RetryScheduled ||
                 outboxEvent.Status ==
                 PaymentOutboxStatus.Processing &&
                 outboxEvent.LeaseExpiresAtUtc <= now))
            .Take(Math.Max(0, limit));
}
