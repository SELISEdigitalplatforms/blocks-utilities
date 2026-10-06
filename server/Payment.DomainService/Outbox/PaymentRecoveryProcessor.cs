using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Outbox;

public sealed class PaymentRecoveryProcessor : IPaymentRecoveryProcessor
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentService _paymentService;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentRecoveryProcessor> _logger;

    public PaymentRecoveryProcessor(
        IPaymentRepository repository,
        IPaymentService paymentService,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentRecoveryProcessor> logger)
    {
        _repository = repository;
        _paymentService = paymentService;
        _options = options;
        _logger = logger;
    }

    public async Task<int> RecoverStaleAsync(string tenantId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var stale = await _repository.GetStaleInitiationsAsync(
            tenantId, now, Math.Clamp(_options.CurrentValue.OutboxBatchSize, 1, 200), cancellationToken);
        var processed = 0;
        foreach (var payment in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Every payment-work message runs this pass, so without a wait here a payment the
            // provider keeps failing is retried as often as work arrives for the tenant -- and its
            // own retries are work. The retry was scheduled for exactly this delay, so skipping
            // loses nothing; the margin only keeps a retry that lands a moment early from being
            // skipped with nothing left to bring it back.
            //
            // Recurring charges only: they are the flow that schedules its retry with this delay.
            // Another flow's retry is scheduled by its own rule, and holding it to this one could
            // skip it with nothing scheduled to bring it back.
            if (payment.PaymentFlow == PaymentFlows.RecurringCharge &&
                payment.PaymentStatus == PaymentStatuses.InitiationUnknown &&
                now < payment.LastUpdatedDateUtc.Add(
                    PaymentRecoveryBackoff.DelayFor(payment.InitiationAttemptCount)) -
                    TimeSpan.FromSeconds(2))
            {
                continue;
            }

            try
            {
                await _paymentService.RecoverAsync(payment, cancellationToken);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("Payment recovery failed PaymentId={PaymentId} TenantId={TenantId} ExceptionType={ExceptionType}",
                    payment.ItemId, tenantId, ex.GetType().Name);
            }
        }
        return processed;
    }
}
