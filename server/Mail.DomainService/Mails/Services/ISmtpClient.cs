using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public interface ISmtpClient
    {
        Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody);
    }
}
