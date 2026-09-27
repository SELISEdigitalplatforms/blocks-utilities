using Blocks.Genesis;
using Sms.DomainService.Dtos;
using Sms.DomainService.Services;
using Sms.DomainService.Utilities;

namespace Sms.Worker.Consumers;

public class SendSmsConsumer : IConsumer<SendSmsCommand>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SendSmsConsumer> _logger;

    public SendSmsConsumer(IServiceScopeFactory scopeFactory, ILogger<SendSmsConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Consume(SendSmsCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.TenantId))
        {
            throw new InvalidOperationException("An SMS send command requires a tenant identifier.");
        }

        using var tenant = SmsTenantContext.Enter(command.TenantId);
        using var logScope = SmsLogScope.Begin(_logger, command.TenantId, command.CorrelationId, command.MessageId);
        using var scope = _scopeFactory.CreateScope();

        try
        {
            await scope.ServiceProvider.GetRequiredService<ISmsProcessingService>().ProcessSendAsync(command.TenantId, command.MessageId, command.CorrelationId);
        }
        catch (Exception ex)
        {
            // Rethrown so the broker redelivers (and dead-letters after its max delivery count).
            // A redelivery is safe: the send lease turns a duplicate into a no-op.
            _logger.LogError(ex, "SendSmsConsumer: SMS send failed");
            throw;
        }
    }
}
