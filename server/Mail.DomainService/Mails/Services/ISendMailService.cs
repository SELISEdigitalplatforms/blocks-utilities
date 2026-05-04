using Mail.DomainService.Dtos;

namespace Mail.DomainService.Mails
{
    public interface ISendMailService
    {
        Task ProcessSendMailAsync(SendEmailEvent sendEmailEvent);
    }
}
