using Blocks.Genesis;
using Sms.DomainService.Dtos;
using Sms.DomainService.Services;

namespace Sms.Worker.Consumers;

public class SendSmsConsumer : IConsumer<SendSmsCommand>
{
    private readonly ISmsProcessingService _smsProcessingService;
    private readonly ILogger<SendSmsConsumer> _logger;

    public SendSmsConsumer(ISmsProcessingService smsProcessingService, ILogger<SendSmsConsumer> logger)
    {
        _smsProcessingService = smsProcessingService;
        _logger = logger;
    }

    public async Task Consume(SendSmsCommand context)
    {
        try
        {
            await _smsProcessingService.ProcessCommandAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendSmsConsumer: unhandled SMS queue failure MessageId={MessageId}, CorrelationId={CorrelationId}", context.MessageId, context.CorrelationId);
        }
    }
}

