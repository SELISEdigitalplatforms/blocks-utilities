using Sms.DomainService.Dtos;

namespace Sms.DomainService.Services;

public interface ISmsProcessingService
{
    Task ProcessCommandAsync(SendSmsCommand command, CancellationToken cancellationToken = default);
    Task ProcessDueRetriesAsync(CancellationToken cancellationToken = default);
    Task ReconcileDeliveryAsync(SmsDeliveryCheckEvent deliveryCheckEvent, CancellationToken cancellationToken = default);
    Task ReconcileSubmittedMessagesAsync(CancellationToken cancellationToken = default);
}



