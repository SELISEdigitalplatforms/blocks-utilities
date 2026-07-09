using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails.Services.Transport
{
    public interface ISmtpClient
    {
        Task<MailSubmissionResult> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody);
    }
}
