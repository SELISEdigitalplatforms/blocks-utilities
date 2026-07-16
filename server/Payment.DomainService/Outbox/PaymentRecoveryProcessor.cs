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
        var stale = await _repository.GetStaleInitiationsAsync(
            tenantId, DateTime.UtcNow, Math.Clamp(_options.CurrentValue.OutboxBatchSize, 1, 200), cancellationToken);
        var processed = 0;
        foreach (var payment in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
