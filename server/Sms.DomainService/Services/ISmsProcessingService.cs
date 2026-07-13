using Sms.DomainService.Dtos;

namespace Sms.DomainService.Services;

public interface ISmsProcessingService
{
    Task ProcessCommandAsync(SendSmsCommand command, CancellationToken cancellationToken = default);
    Task ProcessDueRetriesAsync(string tenantId, CancellationToken cancellationToken = default);
    Task ReconcileDeliveryAsync(SmsDeliveryCheckEvent deliveryCheckEvent, CancellationToken cancellationToken = default);
    Task ReconcileSubmittedMessagesAsync(string tenantId, CancellationToken cancellationToken = default);
}
