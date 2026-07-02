using Mail.DomainService.Dtos;

namespace Mail.DomainService.Mails.Services.Core
{
    public interface ISendMailService
    {
        Task ProcessSendMailAsync(SendEmailCommand sendEmailCommand);
    }
}
