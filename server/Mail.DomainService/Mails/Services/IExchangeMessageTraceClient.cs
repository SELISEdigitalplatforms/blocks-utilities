using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public interface IExchangeMessageTraceClient
    {
        Task<IReadOnlyList<ExchangeMessageTraceResult>> GetDeliveryStatusesAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default);
    }
}
