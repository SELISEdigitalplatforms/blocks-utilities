using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public interface IMailProviderRateLimiter
    {
        Task<MailRateLimitResult> CheckAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default);
    }
}
