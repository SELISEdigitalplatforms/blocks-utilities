using System.Diagnostics;
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
    private readonly IPaymentWorkDispatcher _workDispatcher;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentWebhookProcessor> _logger;

    public PaymentWebhookProcessor(
        IPaymentWebhookInboxRepository inbox,
        IPaymentWebhookStateTransitionService transitions,
        IPaymentWorkDispatcher workDispatcher,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentWebhookProcessor> logger)
    {
        _inbox = inbox;
        _transitions = transitions;
        _workDispatcher = workDispatcher;
        _options = options;
        _logger = logger;
    }

    public async Task<int> ProcessDueAsync(string tenantId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var options = _options.CurrentValue;
        var tenantHash = PaymentLogValue.Hash(tenantId);
        var batchSize = Math.Clamp(options.WebhookBatchSize, 1, 200);

        _logger.LogDebug(
            "Webhook worker scan started TenantHash={TenantHash} BatchSize={BatchSize}",
            tenantHash,
            batchSize);

        var due = await _inbox.GetDueAsync(
            tenantId,
            DateTime.UtcNow,
            batchSize,
            cancellationToken);

        if (due.Count == 0)
        {
            _logger.LogDebug(
                "Webhook worker scan completed TenantHash={TenantHash} DueCount=0 DurationMs={DurationMs}",
                tenantHash,
                stopwatch.Elapsed.TotalMilliseconds);

            return 0;
        }

        _logger.LogInformation(
            "Webhook worker found due records TenantHash={TenantHash} DueCount={DueCount}",
            tenantHash,
            due.Count);

        var processed = 0;

        foreach (var candidate in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemStopwatch = Stopwatch.StartNew();
            var leaseId = Guid.NewGuid().ToString("N");
            var leaseSeconds = Math.Clamp(options.WebhookLeaseSeconds, 10, 300);
            var webhookIdHash = PaymentLogValue.Hash(candidate.WebhookId);

            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantHash"] = tenantHash,
                ["WebhookIdHash"] = webhookIdHash,
                ["WebhookType"] = PaymentLogValue.Label(candidate.WebhookType),
                ["EventCode"] = PaymentLogValue.Label(candidate.EventCode),
                ["PaymentDetailIdHash"] = PaymentLogValue.Hash(
                    candidate.NormalizedPayload.PaymentDetailId),
                ["ProviderEventIdHash"] = PaymentLogValue.Hash(
                    candidate.PspReference ?? candidate.NormalizedPayload.EventId)
            });

            _logger.LogInformation(
                "Webhook worker claim started CurrentStatus={CurrentStatus} AttemptCount={AttemptCount} LeaseSeconds={LeaseSeconds}",
                candidate.Status,
                candidate.AttemptCount,
                leaseSeconds);

            var claimed = await _inbox.TryClaimAsync(
                tenantId,
                candidate.WebhookId,
                leaseId,
                DateTime.UtcNow.AddSeconds(leaseSeconds),
                cancellationToken);

            if (claimed == null)
            {
                _logger.LogInformation(
                    "Webhook worker claim skipped Reason=already_claimed_or_not_due DurationMs={DurationMs}",
                    itemStopwatch.Elapsed.TotalMilliseconds);

                continue;
            }

            _logger.LogInformation(
                "Webhook worker claim acquired LeaseIdHash={LeaseIdHash} LeaseExpiresAtUtc={LeaseExpiresAtUtc}",
                PaymentLogValue.Hash(leaseId),
                claimed.LeaseExpiresAtUtc);

            try
            {
                _logger.LogInformation(
                    "Webhook worker state transition started");

                await _transitions.ApplyAsync(claimed, cancellationToken);

                _logger.LogInformation(
                    "Webhook worker state transition completed; marking inbox record processed");

                await _inbox.MarkProcessedAsync(tenantId, claimed.WebhookId, leaseId, cancellationToken);
                processed++;

                _logger.LogInformation(
                    "Webhook worker record completed FinalStatus=Processed DurationMs={DurationMs}",
                    itemStopwatch.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var attempts = claimed.AttemptCount + 1;
                var status = attempts >= Math.Max(1, options.WebhookMaxAttempts)
                    ? PaymentWebhookStatus.DeadLettered
                    : PaymentWebhookStatus.RetryScheduled;
                var delay = Math.Min(300, (int)Math.Pow(2, Math.Min(attempts, 8)) + Random.Shared.Next(0, 5));
                var nextAttemptAtUtc = DateTime.UtcNow.AddSeconds(delay);

                await _inbox.MarkFailedAsync(
                    tenantId,
                    claimed.WebhookId,
                    leaseId,
                    status,
                    attempts,
                    nextAttemptAtUtc,
                    cancellationToken);

                if (status != PaymentWebhookStatus.DeadLettered)
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

                _logger.LogWarning(
                    ex,
                    "Webhook worker record failed Attempt={Attempt} FinalStatus={Status} RetryDelaySeconds={RetryDelaySeconds} NextAttemptAtUtc={NextAttemptAtUtc} ExceptionType={ExceptionType} DurationMs={DurationMs}",
                    attempts,
                    status,
                    delay,
                    nextAttemptAtUtc,
                    ex.GetType().Name,
                    itemStopwatch.Elapsed.TotalMilliseconds);
            }
        }

        _logger.LogInformation(
            "Webhook worker scan completed TenantHash={TenantHash} DueCount={DueCount} ProcessedCount={ProcessedCount} DurationMs={DurationMs}",
            tenantHash,
            due.Count,
            processed,
            stopwatch.Elapsed.TotalMilliseconds);

        return processed;
    }
}
