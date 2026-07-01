using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public interface ISmtpClient
    {
        Task<MailSubmissionResult> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody);
    }
}
