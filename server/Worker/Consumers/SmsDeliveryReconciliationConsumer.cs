using Blocks.Genesis;
using Sms.DomainService.Dtos;
using Sms.DomainService.Services;

namespace Sms.Worker.Consumers;

public class SmsDeliveryReconciliationConsumer : IConsumer<SmsDeliveryCheckEvent>
{
    private readonly ISmsProcessingService _smsProcessingService;
    private readonly ILogger<SmsDeliveryReconciliationConsumer> _logger;

    public SmsDeliveryReconciliationConsumer(ISmsProcessingService smsProcessingService, ILogger<SmsDeliveryReconciliationConsumer> logger)
    {
        _smsProcessingService = smsProcessingService;
        _logger = logger;
    }

    public async Task Consume(SmsDeliveryCheckEvent context)
    {
        try
        {
            await _smsProcessingService.ReconcileDeliveryAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SmsDeliveryReconciliationConsumer: delivery reconciliation failed MessageId={MessageId}", context.MessageId);
        }
    }
}
