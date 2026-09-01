using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;
using Payment.DomainService.Scheduling;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentMethodSetupExpiryProcessor : IPaymentMethodSetupExpiryProcessor
{
    private readonly IPaymentRepository _payments;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly PaymentWorkMetrics _metrics;
    private readonly ILogger<PaymentMethodSetupExpiryProcessor> _logger;
    private readonly TimeProvider _time;

    public PaymentMethodSetupExpiryProcessor(
        IPaymentRepository payments,
        IOptionsMonitor<PaymentOptions> options,
        PaymentWorkMetrics metrics,
        ILogger<PaymentMethodSetupExpiryProcessor> logger,
        TimeProvider? time = null)
    {
        _payments = payments;
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

            // Recorded for every candidate the sweep looks at, not only the ones it goes on to
            // expire: an operator watching this gauge sees a setup's age climbing well before it
            // crosses the timeout, which is the point at which the *cause* -- Adyen never having
            // sent one of the two webhooks -- is still worth investigating.
            _metrics.RecordSetupPendingAge(age, missingSignal);

            if (!await _payments.TryExpireSetupAsync(tenantId, candidate.ItemId, now, cancellationToken))
            {
                // Lost the compare-and-set: the setup completed, or was declined, between being
                // read as a candidate and this write. Not a failure -- that outcome is strictly
                // better than the one this sweep exists to produce.
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
