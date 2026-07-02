using Mail.DomainService.Dtos;

namespace Mail.DomainService.Mails.Services.DeliveryTracking
{
    public interface IMailDeliveryStatusService
    {
        Task ProcessDeliveryStatusCheckAsync(CheckMailDeliveryStatusCommand command, CancellationToken cancellationToken = default);
    }
}
