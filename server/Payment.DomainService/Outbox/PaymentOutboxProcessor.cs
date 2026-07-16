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
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentOutboxProcessor> _logger;

    public PaymentOutboxProcessor(
        IPaymentRepository repository,
        IMessageClient messageClient,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentOutboxProcessor> logger)
    {
        _repository = repository;
        _messageClient = messageClient;
        _options = options;
        _logger = logger;
    }

    public async Task<int> PublishDueAsync(string tenantId, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;
        var payments = await _repository.GetPaymentsWithDueOutboxEventsAsync(
            tenantId, now, Math.Clamp(options.OutboxBatchSize, 1, 200), cancellationToken);
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
                var leaseId = Guid.NewGuid().ToString("N");
                var leaseUntil = now.AddSeconds(Math.Clamp(options.OutboxLeaseSeconds, 10, 300));
                if (!await _repository.TryClaimOutboxEventAsync(tenantId, payment.ItemId, outboxEvent.EventId, leaseId, leaseUntil, cancellationToken)) continue;
                try
                {
                    await _messageClient.SendToMassConsumerAsync(new ConsumerMessage<PaymentLifecycleEvent>
                    {
                        ConsumerName = PaymentConstants.LifecycleTopic,
                        Payload = outboxEvent.Payload
                    });
                    await _repository.MarkOutboxPublishedAsync(tenantId, payment.ItemId, outboxEvent.EventId, leaseId, DateTime.UtcNow, cancellationToken);
                    published++;
                    _logger.LogInformation("Published payment event PaymentId={PaymentId} EventId={EventId} EventType={EventType} TenantId={TenantId}",
                        payment.ItemId, outboxEvent.EventId, outboxEvent.EventType, tenantId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var attempts = outboxEvent.AttemptCount + 1;
                    var status = attempts >= Math.Max(1, options.OutboxMaxAttempts)
                        ? PaymentOutboxStatus.DeadLettered
                        : PaymentOutboxStatus.RetryScheduled;
                    var delay = Math.Min(300, (int)Math.Pow(2, Math.Min(attempts, 8)) + Random.Shared.Next(0, 5));
                    await _repository.MarkOutboxFailedAsync(tenantId, payment.ItemId, outboxEvent.EventId, leaseId, status,
                        attempts, DateTime.UtcNow.AddSeconds(delay), ex.GetType().Name, cancellationToken);
                    _logger.LogError("Payment event publication failed PaymentId={PaymentId} EventId={EventId} Attempt={Attempt} Status={Status} ExceptionType={ExceptionType}",
                        payment.ItemId, outboxEvent.EventId, attempts, status, ex.GetType().Name);
                }
            }
        }
        return published;
    }
}
