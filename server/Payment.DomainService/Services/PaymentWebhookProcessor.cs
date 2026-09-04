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

    public async Task<PaymentWebhookProcessingResult> ProcessDueAsync(string tenantId, CancellationToken cancellationToken)
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

            return PaymentWebhookProcessingResult.Empty;
        }

        _logger.LogInformation(
            "Webhook worker found due records TenantHash={TenantHash} DueCount={DueCount}",
            tenantHash,
            due.Count);

        var processed = 0;

        // The payments this pass actually transitioned, in the order they were applied. Ordinal
        // dedupe because two events for one payment in a single batch is ordinary — an
        // authorisation and its capture, say — and the caller should look the link up once.
        var transitioned = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemStopwatch = Stopwatch.StartNew();
            var leaseId = Guid.NewGuid().ToString("N");
            var leaseSeconds = Math.Clamp(options.WebhookLeaseSeconds, 10, 300);
            var webhookIdHash = PaymentLogValue.Hash(candidate.WebhookId);

            // The correlation of the provider request that delivered this event, so intake and
            // this run — separated by the queue, often by minutes — read as one story.
            using var correlation = PaymentCorrelation.Begin(
                candidate.CorrelationId);
            using var scope = PaymentLogScope.Begin(
                _logger,
                PaymentOperations.WebhookProcess,
                tenantId,
                candidate.NormalizedPayload.PaymentDetailId,
                extra: new Dictionary<string, object?>
                {
                    ["WebhookId"] = PaymentLogValue.Id(candidate.WebhookId),
                    ["WebhookType"] = PaymentLogValue.Label(candidate.WebhookType),
                    ["EventCode"] = PaymentLogValue.Label(candidate.EventCode),
                    ["ProviderEventId"] = PaymentLogValue.Id(
                        candidate.PspReference ??
                        candidate.NormalizedPayload.EventId)
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

                // Only after the record is marked processed: an id reported for a transition that
                // then failed to commit would send the caller looking for a confirmation that
                // does not exist.
                var paymentDetailId = claimed.NormalizedPayload.PaymentDetailId;

                if (!string.IsNullOrWhiteSpace(paymentDetailId) &&
                    seen.Add(paymentDetailId))
                {
                    transitioned.Add(paymentDetailId);
                }

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

        return new PaymentWebhookProcessingResult(processed, transitioned);
    }
}
