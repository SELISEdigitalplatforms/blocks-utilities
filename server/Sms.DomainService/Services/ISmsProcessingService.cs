using Sms.DomainService.Dtos;

namespace Sms.DomainService.Services;

public interface ISmsProcessingService
{
    Task ProcessOutboxMessageAsync(ProcessSmsOutboxMessageCommand command, CancellationToken cancellationToken = default);
    Task ProcessCommandAsync(SendSmsCommand command, CancellationToken cancellationToken = default);
    Task ProcessDueRetriesAsync(string tenantId, TimeSpan queueRecoveryGracePeriod, CancellationToken cancellationToken = default);
    Task ReconcileDeliveryAsync(SmsDeliveryCheckEvent deliveryCheckEvent, CancellationToken cancellationToken = default);
    Task ReconcileSubmittedMessagesAsync(string tenantId, CancellationToken cancellationToken = default);
}
