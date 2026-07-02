using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails.Services.RateLimiting
{
    public interface IMailProviderRateLimiter
    {
        Task<MailRateLimitResult> CheckAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default);
    }
}
