using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Commands;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentWorkDispatcher : IPaymentWorkDispatcher
{
    private readonly IMessageClient _messageClient;
    private readonly IPaymentTenantContextScopeFactory _contexts;
    private readonly ILogger<PaymentWorkDispatcher> _logger;

    public PaymentWorkDispatcher(
        IMessageClient messageClient,
        IPaymentTenantContextScopeFactory contexts,
        ILogger<PaymentWorkDispatcher> logger)
    {
        _messageClient = messageClient;
        _contexts = contexts;
        _logger = logger;
    }

    public async Task DispatchAsync(
        string tenantId,
        bool includeRecovery,
        DateTimeOffset? scheduledAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var context = _contexts.Establish(tenantId);

        await _messageClient.SendToMassConsumerAsync(
            new ConsumerMessage<ProcessPaymentWorkCommand>
            {
                ConsumerName =
                    PaymentConstants.PaymentWorkQueue,
                Payload = new ProcessPaymentWorkCommand
                {
                    TenantId = tenantId,
                    IncludeRecovery = includeRecovery
                },
                ScheduledEnqueueTimeUtc = scheduledAtUtc
            });

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<bool> TryDispatchAsync(
        string tenantId,
        bool includeRecovery,
        DateTimeOffset? scheduledAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await DispatchAsync(
                tenantId,
                includeRecovery,
                scheduledAtUtc,
                cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Payment work dispatch failed TenantHash={TenantHash} IncludeRecovery={IncludeRecovery} Scheduled={Scheduled}",
                PaymentLogValue.Hash(tenantId),
                includeRecovery,
                scheduledAtUtc.HasValue);

            return false;
        }
    }
}
