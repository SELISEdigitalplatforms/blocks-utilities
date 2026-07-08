using Blocks.Genesis;
using Sms.DomainService.Dtos;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Services;

public class SmsEventPublisher : ISmsEventPublisher
{
    private readonly IMessageClient _messageClient;

    public SmsEventPublisher(IMessageClient messageClient)
    {
        _messageClient = messageClient;
    }

    public Task PublishStatusAsync(SmsStatusEvent statusEvent, CancellationToken cancellationToken = default)
    {
        return _messageClient.SendToMassConsumerAsync(new ConsumerMessage<SmsStatusEvent>
        {
            ConsumerName = SmsConstants.SmsStatusTopic,
            Payload = statusEvent
        });
    }
}
