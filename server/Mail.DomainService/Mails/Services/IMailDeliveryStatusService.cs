using Mail.DomainService.Dtos;

namespace Mail.DomainService.Mails
{
    public interface IMailDeliveryStatusService
    {
        Task ProcessDeliveryStatusCheckAsync(CheckMailDeliveryStatusCommand command, CancellationToken cancellationToken = default);
    }
}
