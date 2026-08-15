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

        // Read from the ambient flow rather than taken as an argument: this is called from
        // twenty-three places, and a parameter would carry the value only through the ones
        // somebody remembered to pass it at.
        var correlationId = PaymentCorrelation.Current;

        await _messageClient.SendToMassConsumerAsync(
            new ConsumerMessage<ProcessPaymentWorkCommand>
            {
                ConsumerName =
                    PaymentConstants.PaymentWorkQueue,
                Payload = new ProcessPaymentWorkCommand
                {
                    TenantId = tenantId,
                    IncludeRecovery = includeRecovery,
                    CorrelationId = correlationId,
                    DispatchedAtUtc = DateTime.UtcNow
                },
                ScheduledEnqueueTimeUtc = scheduledAtUtc
            });

        // Logged so the queue hop has a start as well as an end. Without this line the consumer
        // reports work arriving that nothing is recorded as having asked for.
        _logger.LogInformation(
            "Payment work dispatched Operation={Operation} Phase={Phase} CorrelationId={CorrelationId} TenantHash={TenantHash} IncludeRecovery={IncludeRecovery} Scheduled={Scheduled}",
            PaymentOperations.WorkDispatch,
            PaymentPhases.Completed,
            PaymentLogValue.Id(correlationId),
            PaymentLogValue.Hash(tenantId),
            includeRecovery,
            scheduledAtUtc.HasValue);

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
                "Payment work dispatch failed Operation={Operation} Phase={Phase} CorrelationId={CorrelationId} TenantHash={TenantHash} IncludeRecovery={IncludeRecovery} Scheduled={Scheduled}",
                PaymentOperations.WorkDispatch,
                PaymentPhases.Failed,
                PaymentLogValue.Id(PaymentCorrelation.Current),
                PaymentLogValue.Hash(tenantId),
                includeRecovery,
                scheduledAtUtc.HasValue);

            return false;
        }
    }
}
