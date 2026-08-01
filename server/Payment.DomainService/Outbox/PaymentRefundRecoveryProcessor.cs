using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Outbox;

public sealed class PaymentRefundRecoveryProcessor :
    IPaymentRefundRecoveryProcessor
{
    private readonly IPaymentRefundRepository _refunds;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentRefundInitiationService _initiation;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentRefundRecoveryProcessor>
        _logger;

    public PaymentRefundRecoveryProcessor(
        IPaymentRefundRepository refunds,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentRefundInitiationService initiation,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentRefundRecoveryProcessor> logger)
    {
        _refunds = refunds;
        _payments = payments;
        _providers = providers;
        _minorUnits = minorUnits;
        _initiation = initiation;
        _options = options;
        _logger = logger;
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
        var payments =
            await _refunds
                .GetPaymentsWithDueRefundInitiationsAsync(
                    tenantId,
                    now,
                    batchSize,
                    cancellationToken);
        var processed = 0;

        foreach (var payment in payments)
        {
            foreach (var refund in payment.Refunds
                         .Where(candidate =>
                             (candidate.Status is
                                 PaymentRefundStatuses
                                     .Initiating or
                                 PaymentRefundStatuses
                                     .InitiationUnknown) &&
                             (candidate
                                  .NextRecoveryAttemptAtUtc ==
                              null ||
                              candidate
                                  .NextRecoveryAttemptAtUtc <=
                              now))
                         .Take(batchSize - processed))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (processed >= batchSize)
                {
                    return processed;
                }

                var maximumAttempts = Math.Clamp(
                    _options.CurrentValue
                        .RefundRecoveryMaxAttempts,
                    1,
                    20);

                if (refund.InitiationAttemptCount >=
                    maximumAttempts)
                {
                    await _refunds.MarkRequiresAttentionAsync(
                        tenantId,
                        payment.ItemId,
                        refund.RefundId,
                        null,
                        "payment_refund_recovery_exhausted",
                        cancellationToken);

                    _logger.LogCritical(
                        "Payment refund recovery exhausted TenantHash={TenantHash} PaymentHash={PaymentHash} RefundHash={RefundHash} AttemptCount={AttemptCount}",
                        PaymentLogValue.Hash(tenantId),
                        PaymentLogValue.Hash(payment.ItemId),
                        PaymentLogValue.Hash(refund.RefundId),
                        refund.InitiationAttemptCount);

                    processed++;
                    continue;
                }

                var leaseId = Guid.NewGuid().ToString("N");
                var claimed =
                    await _refunds.TryClaimInitiationAsync(
                        tenantId,
                        payment.ItemId,
                        refund.RefundId,
                        leaseId,
                        now.Add(
                            PaymentRefundLeasePolicy.Resolve(
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
                    await _refunds.MarkRequiresAttentionAsync(
                        tenantId,
                        payment.ItemId,
                        claimed.RefundId,
                        leaseId,
                        "payment_refund_recovery_unavailable",
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
