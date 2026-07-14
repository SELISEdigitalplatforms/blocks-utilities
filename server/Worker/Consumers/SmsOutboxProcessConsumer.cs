using Blocks.Genesis;
using Sms.DomainService.Dtos;
using Sms.DomainService.Services;

namespace Sms.Worker.Consumers;

public class SmsOutboxProcessConsumer : IConsumer<ProcessSmsOutboxMessageCommand>
{
    private readonly ISmsProcessingService _smsProcessingService;
    private readonly ILogger<SmsOutboxProcessConsumer> _logger;

    public SmsOutboxProcessConsumer(ISmsProcessingService smsProcessingService, ILogger<SmsOutboxProcessConsumer> logger)
    {
        _smsProcessingService = smsProcessingService;
        _logger = logger;
    }

    public async Task Consume(ProcessSmsOutboxMessageCommand command)
    {
        try
        {
            await _smsProcessingService.ProcessOutboxMessageAsync(command);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SmsOutboxProcessConsumer: outbox processing failed OutboxMessageId={OutboxMessageId}, TenantId={TenantId}, CorrelationId={CorrelationId}",
                command.OutboxMessageId,
                command.TenantId,
                command.CorrelationId);
        }
    }
}
