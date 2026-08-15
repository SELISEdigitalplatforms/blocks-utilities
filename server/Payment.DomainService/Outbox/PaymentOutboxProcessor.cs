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

public sealed class PaymentOutboxProcessor : IPaymentOutboxProcessor
{
    private readonly IPaymentRepository _repository;
    private readonly IMessageClient _messageClient;
    private readonly IPaymentWorkDispatcher _workDispatcher;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentOutboxProcessor> _logger;

    public PaymentOutboxProcessor(
        IPaymentRepository repository,
        IMessageClient messageClient,
        IPaymentWorkDispatcher workDispatcher,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentOutboxProcessor> logger)
    {
        _repository = repository;
        _messageClient = messageClient;
        _workDispatcher = workDispatcher;
        _options = options;
        _logger = logger;
    }

    public async Task<int> PublishDueAsync(string tenantId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;
        var tenantHash = PaymentLogValue.Hash(tenantId);
        var batchSize = Math.Clamp(options.OutboxBatchSize, 1, 200);

        _logger.LogDebug(
            "Payment outbox scan started TenantHash={TenantHash} BatchSize={BatchSize}",
            tenantHash,
            batchSize);

        var payments = await _repository.GetPaymentsWithDueOutboxEventsAsync(
            tenantId,
            now,
            batchSize,
            cancellationToken);

        if (payments.Count == 0)
        {
            _logger.LogDebug(
                "Payment outbox scan completed TenantHash={TenantHash} PaymentCount=0 DurationMs={DurationMs}",
                tenantHash,
                stopwatch.Elapsed.TotalMilliseconds);

            return 0;
        }

        _logger.LogInformation(
            "Payment outbox found payments with due events TenantHash={TenantHash} PaymentCount={PaymentCount}",
            tenantHash,
            payments.Count);

        var published = 0;

        foreach (var payment in payments)
        {
            foreach (var outboxEvent in payment.OutboxEvents
                .Where(x => x.Status is PaymentOutboxStatus.Pending or PaymentOutboxStatus.RetryScheduled ||
                            x.Status == PaymentOutboxStatus.Processing && x.LeaseExpiresAtUtc <= now)
                .Where(x => x.NextAttemptAtUtc <= now)
                .Take(10))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventStopwatch = Stopwatch.StartNew();
                var leaseId = Guid.NewGuid().ToString("N");
                var leaseUntil = now.AddSeconds(Math.Clamp(options.OutboxLeaseSeconds, 10, 300));

                // Re-established per event from the payload the payment was created with, so a
                // publish minutes or hours later still logs under the request that caused it.
                // The value was always carried here; it just never reached the logs.
                using var correlation = PaymentCorrelation.Begin(
                    outboxEvent.Payload.CorrelationId);
                using var scope = PaymentLogScope.Begin(
                    _logger,
                    PaymentOperations.OutboxPublish,
                    tenantId,
                    payment.ItemId,
                    extra: new Dictionary<string, object?>
                    {
                        ["OutboxEventId"] = PaymentLogValue.Id(outboxEvent.EventId),
                        ["OutboxEventType"] = PaymentLogValue.Label(outboxEvent.EventType),
                        ["OrderId"] = PaymentLogValue.Id(payment.OrderId)
                    });

                _logger.LogInformation(
                    "Payment outbox event claim started CurrentStatus={CurrentStatus} AttemptCount={AttemptCount} LeaseExpiresAtUtc={LeaseExpiresAtUtc}",
                    outboxEvent.Status,
                    outboxEvent.AttemptCount,
                    leaseUntil);

                var claimed = await _repository.TryClaimOutboxEventAsync(
                    tenantId,
                    payment.ItemId,
                    outboxEvent.EventId,
                    leaseId,
                    leaseUntil,
                    cancellationToken);

                if (!claimed)
                {
                    _logger.LogInformation(
                        "Payment outbox event claim skipped Reason=already_claimed_or_not_due DurationMs={DurationMs}",
                        eventStopwatch.Elapsed.TotalMilliseconds);

                    continue;
                }

                _logger.LogInformation(
                    "Payment outbox event claim acquired LeaseIdHash={LeaseIdHash}; publishing Topic={Topic}",
                    PaymentLogValue.Hash(leaseId),
                    PaymentConstants.LifecycleTopic);

                try
                {
                    await _messageClient.SendToMassConsumerAsync(new ConsumerMessage<PaymentLifecycleEvent>
                    {
                        ConsumerName = PaymentConstants.LifecycleTopic,
                        Payload = outboxEvent.Payload
                    });

                    _logger.LogInformation(
                        "Payment outbox broker publish completed; marking event published");

                    await _repository.MarkOutboxPublishedAsync(
                        tenantId,
                        payment.ItemId,
                        outboxEvent.EventId,
                        leaseId,
                        DateTime.UtcNow,
                        cancellationToken);

                    published++;

                    _logger.LogInformation(
                        "Payment outbox event completed FinalStatus=Published DurationMs={DurationMs}",
                        eventStopwatch.Elapsed.TotalMilliseconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var attempts = outboxEvent.AttemptCount + 1;
                    var status = attempts >= Math.Max(1, options.OutboxMaxAttempts)
                        ? PaymentOutboxStatus.DeadLettered
                        : PaymentOutboxStatus.RetryScheduled;
                    var delay = Math.Min(300, (int)Math.Pow(2, Math.Min(attempts, 8)) + Random.Shared.Next(0, 5));
                    var nextAttemptAtUtc = DateTime.UtcNow.AddSeconds(delay);

                    await _repository.MarkOutboxFailedAsync(
                        tenantId,
                        payment.ItemId,
                        outboxEvent.EventId,
                        leaseId,
                        status,
                        attempts,
                        nextAttemptAtUtc,
                        ex.GetType().Name,
                        cancellationToken);

                    if (status !=
                        PaymentOutboxStatus.DeadLettered)
                    {
                        await _workDispatcher.TryDispatchAsync(
                            tenantId,
                            includeRecovery: false,
                            scheduledAtUtc:
                                new DateTimeOffset(
                                    nextAttemptAtUtc,
                                    TimeSpan.Zero),
                            cancellationToken:
                                cancellationToken);
                    }

                    _logger.LogError(
                        ex,
                        "Payment outbox event failed Attempt={Attempt} FinalStatus={Status} RetryDelaySeconds={RetryDelaySeconds} NextAttemptAtUtc={NextAttemptAtUtc} ExceptionType={ExceptionType} DurationMs={DurationMs}",
                        attempts,
                        status,
                        delay,
                        nextAttemptAtUtc,
                        ex.GetType().Name,
                        eventStopwatch.Elapsed.TotalMilliseconds);
                }
            }
        }

        _logger.LogInformation(
            "Payment outbox scan completed TenantHash={TenantHash} PaymentCount={PaymentCount} PublishedCount={PublishedCount} DurationMs={DurationMs}",
            tenantHash,
            payments.Count,
            published,
            stopwatch.Elapsed.TotalMilliseconds);

        return published;
    }
}
