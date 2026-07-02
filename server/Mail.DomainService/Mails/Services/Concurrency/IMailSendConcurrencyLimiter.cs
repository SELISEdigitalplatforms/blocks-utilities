using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails.Services.Concurrency
{
    public interface IMailSendConcurrencyLimiter
    {
        Task<IAsyncDisposable> AcquireAsync(MailCategory mailCategory, CancellationToken cancellationToken = default);
    }
}
