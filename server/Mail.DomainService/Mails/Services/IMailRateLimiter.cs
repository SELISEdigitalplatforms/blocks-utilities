using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public interface IMailRateLimiter
    {
        Task<MailRateLimitResult> CheckAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default);
    }
}
