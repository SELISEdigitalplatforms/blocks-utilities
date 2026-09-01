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

        // Two different concerns that used to share one capped query -- see PR #393 review
        // (Finding, round 5) and IPaymentRepository.GetSetupsReadyForCompletionAsync's remarks --
        // now run as two independent passes so a ready setup can never be starved behind an
        // unrelated backlog of older, still-incomplete setups the pending-age observation is
        // busy looking at.
        await CompleteReadySetupsAsync(tenantId, now, batchSize, cancellationToken);
        await RecordPendingAgeAsync(tenantId, now, cancellationToken);

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
    /// Completes a setup that already has both signals recorded but is still Processing -- see
    /// PR #393 review (Finding 1)'s residual gap: both
    /// <see cref="PaymentMethodSetupWebhookStateTransitionService"/> and
    /// <see cref="StoredPaymentMethodLifecycleService"/> already retry
    /// <see cref="PaymentMethodSetupCompletion.TryCompleteAsync"/> every time either signal's own
    /// webhook is processed, including on a redelivery, so this only ever has work to do when a
    /// process crashed between recording the final signal and calling that completion, with no
    /// further webhook redelivery left to retry it.
    /// </summary>
    /// <remarks>
    /// See PR #393 review (Finding, round 5): this pages through
    /// <see cref="IPaymentRepository.GetSetupsReadyForCompletionAsync"/> rather than taking a
    /// single capped batch, because that query is deliberately not bounded the way an
    /// expiry-candidate sweep is -- a tenant can have more ready setups than one batch, and every
    /// one of them is, by definition, actionable right now. Paging stops once a page comes back
    /// smaller than <paramref name="batchSize"/> (nothing left to find) or a full page completed
    /// nothing at all (a defensive stop against looping forever on records this call cannot
    /// actually advance, which should not happen given <see cref="PaymentMethodSetupCompletion"/>'s
    /// own idempotency, but costs nothing to guard against).
    /// </remarks>
    private async Task CompleteReadySetupsAsync(
        string tenantId,
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ready = await _payments.GetSetupsReadyForCompletionAsync(
                tenantId, batchSize, cancellationToken) ?? [];

            if (ready.Count == 0)
            {
                return;
            }

            var completedAny = false;

            foreach (var setup in ready)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var completed = await PaymentMethodSetupCompletion.TryCompleteAsync(
                    _payments,
                    _events,
                    tenantId,
                    setup,
                    now,
                    cancellationToken);

                if (completed)
                {
                    completedAny = true;

                    _logger.LogWarning(
                        "Card setup completed by recovery sweep after both signals were already " +
                        "on record TenantHash={TenantHash} PaymentHash={PaymentHash}",
                        PaymentLogValue.Hash(tenantId),
                        PaymentLogValue.Hash(setup.ItemId));
                }
            }

            if (!completedAny || ready.Count < batchSize)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Publishes <c>payment.setup.pending_age</c> for every missing-signal category from a single
    /// Mongo aggregation over the tenant's currently pending setups -- see PR #393 review
    /// (Finding, round 5) and
    /// <see cref="IPaymentRepository.GetPendingSetupAgeSummaryAsync"/>'s remarks: the metric used
    /// to be recorded per document from the same capped batch <see cref="CompleteReadySetupsAsync"/>
    /// now owns exclusively, so it only ever reflected whatever fit in that one batch. The
    /// aggregation covers every pending setup for the tenant regardless of how many there are, so
    /// the age recorded for a category is genuinely the oldest offender in it.
    /// </summary>
    private async Task RecordPendingAgeAsync(
        string tenantId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var summary = await _payments.GetPendingSetupAgeSummaryAsync(tenantId, cancellationToken)
            ?? [];

        foreach (var category in summary)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The oldest offender in the category, not every individual setup in it: an operator
            // watching this gauge cares about "how stale is the worst one right now", and Mongo
            // already computed that across the whole tenant rather than a capped batch.
            _metrics.RecordSetupPendingAge(now - category.OldestCreatedAtUtc, category.MissingSignal);
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
