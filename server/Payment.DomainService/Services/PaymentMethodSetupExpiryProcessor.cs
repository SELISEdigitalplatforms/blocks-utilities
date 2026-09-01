using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Scheduling;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentMethodSetupExpiryProcessor : IPaymentMethodSetupExpiryProcessor
{
    private readonly IPaymentRepository _payments;
    private readonly IPaymentOutboxEventFactory _events;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly PaymentWorkMetrics _metrics;
    private readonly ILogger<PaymentMethodSetupExpiryProcessor> _logger;
    private readonly TimeProvider _time;

    public PaymentMethodSetupExpiryProcessor(
        IPaymentRepository payments,
        IPaymentOutboxEventFactory events,
        IOptionsMonitor<PaymentOptions> options,
        PaymentWorkMetrics metrics,
        ILogger<PaymentMethodSetupExpiryProcessor> logger,
        TimeProvider? time = null)
    {
        _payments = payments;
        _events = events;
        _options = options;
        _metrics = metrics;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> ExpireDueAsync(string tenantId, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;
        var cutoff = now.AddSeconds(-Math.Max(60, options.PaymentMethodSetupTimeoutSeconds));
        var batchSize = Math.Clamp(options.WebhookBatchSize, 1, 200);

        await CompleteStuckReadySetupsAndRecordPendingAgeAsync(tenantId, now, batchSize, cancellationToken);

        var candidates = await _payments.GetDueSetupExpiryCandidatesAsync(
            tenantId,
            cutoff,
            batchSize,
            cancellationToken);

        var expired = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var age = now - candidate.CreatedAtUtc;
            var missingSignal = MissingSignalOf(candidate);

            if (!await _payments.TryExpireSetupAsync(tenantId, candidate.ItemId, now, cancellationToken))
            {
                // Lost the compare-and-set: the setup completed, or was declined, between being
                // read as a candidate and this write -- or the still-missing-signal condition the
                // repository re-checks atomically alongside the status no longer held (see
                // PaymentRepository.TryExpireSetupAsync, PR #393 review Finding 1). Not a
                // failure -- that outcome is strictly better than the one this sweep exists to
                // produce.
                continue;
            }

            expired++;
            _metrics.RecordSetupExpired(missingSignal);

            _logger.LogWarning(
                "Card setup expired by recovery sweep TenantHash={TenantHash} PaymentHash={PaymentHash} " +
                "AgeSeconds={AgeSeconds} MissingSignal={MissingSignal}",
                PaymentLogValue.Hash(tenantId),
                PaymentLogValue.Hash(candidate.ItemId),
                age.TotalSeconds,
                missingSignal);
        }

        if (candidates.Count > 0)
        {
            _logger.LogInformation(
                "Card setup expiry sweep completed TenantHash={TenantHash} CandidateCount={CandidateCount} " +
                "ExpiredCount={ExpiredCount}",
                PaymentLogValue.Hash(tenantId),
                candidates.Count,
                expired);
        }

        return expired;
    }

    /// <summary>
    /// Looks at every currently pending setup -- not just the ones already due for expiry -- for
    /// two things <see cref="IPaymentRepository.GetDueSetupExpiryCandidatesAsync"/> alone cannot
    /// give the sweep. See PR #393 review (Finding 3): <c>payment.setup.pending_age</c> is
    /// recorded here for a setup still missing a signal at any age, not only once it has crossed
    /// the timeout, so the metric can actually show age climbing over time rather than only ever
    /// observing setups already at the cliff edge.
    /// </summary>
    /// <remarks>
    /// Also completes a setup that already has both signals recorded but is still Processing --
    /// see PR #393 review (Finding 1)'s residual gap: both
    /// <see cref="PaymentMethodSetupWebhookStateTransitionService"/> and
    /// <see cref="StoredPaymentMethodLifecycleService"/> already retry
    /// <see cref="PaymentMethodSetupCompletion.TryCompleteAsync"/> every time either signal's own
    /// webhook is processed, including on a redelivery, so this only ever has work to do when a
    /// process crashed between recording the final signal and calling that completion, with no
    /// further webhook redelivery left to retry it.
    /// </remarks>
    private async Task CompleteStuckReadySetupsAndRecordPendingAgeAsync(
        string tenantId,
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var pending = await _payments.GetPendingSetupsAsync(tenantId, batchSize, cancellationToken)
            ?? [];

        foreach (var setup in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var missingSignal = MissingSignalOf(setup);

            if (missingSignal == "none")
            {
                var completed = await PaymentMethodSetupCompletion.TryCompleteAsync(
                    _payments,
                    _events,
                    tenantId,
                    setup,
                    now,
                    cancellationToken);

                if (completed)
                {
                    _logger.LogWarning(
                        "Card setup completed by recovery sweep after both signals were already " +
                        "on record TenantHash={TenantHash} PaymentHash={PaymentHash}",
                        PaymentLogValue.Hash(tenantId),
                        PaymentLogValue.Hash(setup.ItemId));
                }

                continue;
            }

            // Recorded for every currently pending setup missing a signal, not only the ones
            // already due for expiry: an operator watching this gauge sees a setup's age
            // climbing well before it crosses the timeout, which is the point at which the
            // *cause* -- Adyen never having sent one of the two webhooks -- is still worth
            // investigating.
            _metrics.RecordSetupPendingAge(now - setup.CreatedAtUtc, missingSignal);
        }
    }

    /// <summary>
    /// Which of the two independent completion signals -- see
    /// <see cref="PaymentMethodSetupWebhookStateTransitionService"/> -- a still-pending setup is
    /// missing. Reported as "both" rather than picking one arbitrarily when neither has arrived,
    /// since that is a materially different failure (nothing at all came back from the provider)
    /// from either signal arriving alone.
    /// </summary>
    private static string MissingSignalOf(PaymentDetail payment) =>
        (payment.SetupAuthorizationConfirmedAtUtc is null, payment.SetupTokenConfirmedAtUtc is null) switch
        {
            (true, true) => "both",
            (true, false) => "authorization",
            (false, true) => "token",
            (false, false) => "none"
        };
}
