using Sms.DomainService.Dtos;

namespace Sms.DomainService.Services;

public interface ISmsEventPublisher
{
    Task PublishStatusAsync(SmsStatusEvent statusEvent, CancellationToken cancellationToken = default);
}
