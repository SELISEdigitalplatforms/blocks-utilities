using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentWebhookProcessor : IPaymentWebhookProcessor
{
    private readonly IPaymentWebhookInboxRepository _inbox;
    private readonly IPaymentWebhookStateTransitionService _transitions;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentWebhookProcessor> _logger;

    public PaymentWebhookProcessor(
        IPaymentWebhookInboxRepository inbox,
        IPaymentWebhookStateTransitionService transitions,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentWebhookProcessor> logger)
    {
        _inbox = inbox;
        _transitions = transitions;
        _options = options;
        _logger = logger;
    }

    public async Task<int> ProcessDueAsync(string tenantId, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var due = await _inbox.GetDueAsync(tenantId, DateTime.UtcNow, Math.Clamp(options.WebhookBatchSize, 1, 200), cancellationToken);
        var processed = 0;
        foreach (var candidate in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaseId = Guid.NewGuid().ToString("N");
            var claimed = await _inbox.TryClaimAsync(tenantId, candidate.WebhookId, leaseId,
                DateTime.UtcNow.AddSeconds(Math.Clamp(options.WebhookLeaseSeconds, 10, 300)), cancellationToken);
            if (claimed == null) continue;
            try
            {
                await _transitions.ApplyAsync(claimed, cancellationToken);
                await _inbox.MarkProcessedAsync(tenantId, claimed.WebhookId, leaseId, cancellationToken);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var attempts = claimed.AttemptCount + 1;
                var status = attempts >= Math.Max(1, options.WebhookMaxAttempts)
                    ? PaymentWebhookStatus.DeadLettered
                    : PaymentWebhookStatus.RetryScheduled;
                var delay = Math.Min(300, (int)Math.Pow(2, Math.Min(attempts, 8)) + Random.Shared.Next(0, 5));
                await _inbox.MarkFailedAsync(tenantId, claimed.WebhookId, leaseId, status, attempts, DateTime.UtcNow.AddSeconds(delay), cancellationToken);
                _logger.LogWarning("Payment webhook processing failed TenantHash={TenantHash} Type={Type} Attempt={Attempt} Status={Status} ExceptionType={ExceptionType}",
                    PaymentHashing.HashSensitiveValue(tenantId)[..16], claimed.EventCode, attempts, status, ex.GetType().Name);
            }
        }
        return processed;
    }
}
