using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Outbox;

public sealed class PaymentCaptureRecoveryProcessor :
    IPaymentCaptureRecoveryProcessor
{
    private readonly IPaymentCaptureRepository _captures;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentCaptureInitiationService _initiation;
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public PaymentCaptureRecoveryProcessor(
        IPaymentCaptureRepository captures,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentCaptureInitiationService initiation,
        IOptionsMonitor<PaymentOptions> options)
    {
        _captures = captures;
        _payments = payments;
        _providers = providers;
        _minorUnits = minorUnits;
        _initiation = initiation;
        _options = options;
    }

    public async Task<int> RecoverDueAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var batchSize = Math.Clamp(
            _options.CurrentValue.OutboxBatchSize,
            1,
            200);
        var payments = await _captures
            .GetPaymentsWithDueCaptureInitiationsAsync(
                tenantId,
                now,
                batchSize,
                cancellationToken);
        var processed = 0;

        foreach (var payment in payments)
        {
            foreach (var capture in payment.Captures
                         .Where(candidate =>
                             candidate.Status is
                                 PaymentCaptureStatuses.Initiating or
                                 PaymentCaptureStatuses.InitiationUnknown &&
                             (candidate.NextRecoveryAttemptAtUtc == null ||
                              candidate.NextRecoveryAttemptAtUtc <= now))
                         .Take(batchSize - processed))
            {
                if (processed >= batchSize)
                {
                    return processed;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (capture.InitiationAttemptCount >= Math.Clamp(
                        _options.CurrentValue.CaptureRecoveryMaxAttempts,
                        1,
                        20))
                {
                    await _captures.MarkRequiresAttentionAsync(
                        tenantId,
                        payment.ItemId,
                        capture.CaptureId,
                        null,
                        "payment_capture_recovery_exhausted",
                        cancellationToken);
                    processed++;
                    continue;
                }

                var leaseId = Guid.NewGuid().ToString("N");
                var claimed = await _captures.TryClaimInitiationAsync(
                    tenantId,
                    payment.ItemId,
                    capture.CaptureId,
                    leaseId,
                    now.Add(PaymentCaptureLeasePolicy.Resolve(
                        _options.CurrentValue)),
                    cancellationToken);

                if (claimed == null)
                {
                    continue;
                }

                var provider = await _providers.GetAsync(
                    tenantId,
                    payment.OrganizationId,
                    claimed.ProviderName,
                    () => _payments.GetProviderAsync(
                        tenantId,
                        payment.OrganizationId,
                        claimed.ProviderName,
                        cancellationToken));

                if (provider == null ||
                    !provider.IsEnabled ||
                    !_minorUnits.TryConvert(
                        claimed.Amount,
                        claimed.CurrencyCode,
                        out var minorUnits))
                {
                    await _captures.MarkRequiresAttentionAsync(
                        tenantId,
                        payment.ItemId,
                        claimed.CaptureId,
                        leaseId,
                        "payment_capture_recovery_unavailable",
                        cancellationToken);
                    processed++;
                    continue;
                }

                await _initiation.SubmitAsync(
                    payment,
                    claimed,
                    provider,
                    leaseId,
                    minorUnits,
                    claimed.CorrelationId,
                    cancellationToken);
                processed++;
            }
        }

        return processed;
    }
}
