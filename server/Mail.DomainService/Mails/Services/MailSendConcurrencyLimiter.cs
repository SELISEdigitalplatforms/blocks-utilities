using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Configuration;

namespace Mail.DomainService.Mails
{
    public class MailSendConcurrencyLimiter : IMailSendConcurrencyLimiter
    {
        private readonly Lazy<SemaphoreSlim> _noAttachmentLimiter;
        private readonly Lazy<SemaphoreSlim> _smallAttachmentLimiter;
        private readonly Lazy<SemaphoreSlim> _largeAttachmentLimiter;

        public MailSendConcurrencyLimiter(IConfiguration configuration)
        {
            _noAttachmentLimiter = new Lazy<SemaphoreSlim>(() => CreateLimiter(configuration, "MicrosoftGraphMail:NoAttachmentMaxConcurrentSends", 15));
            _smallAttachmentLimiter = new Lazy<SemaphoreSlim>(() => CreateLimiter(configuration, "MicrosoftGraphMail:SmallAttachmentMaxConcurrentSends", 8));
            _largeAttachmentLimiter = new Lazy<SemaphoreSlim>(() => CreateLimiter(configuration, "MicrosoftGraphMail:LargeAttachmentMaxConcurrentSends", 2));
        }

        public async Task<IAsyncDisposable> AcquireAsync(MailCategory mailCategory, CancellationToken cancellationToken = default)
        {
            var limiter = GetLimiter(mailCategory);
            await limiter.WaitAsync(cancellationToken);

            return new ConcurrencyLease(limiter);
        }

        private SemaphoreSlim GetLimiter(MailCategory mailCategory)
        {
            return mailCategory switch
            {
                MailCategory.SmallAttachment => _smallAttachmentLimiter.Value,
                MailCategory.LargeAttachment => _largeAttachmentLimiter.Value,
                _ => _noAttachmentLimiter.Value
            };
        }

        private static SemaphoreSlim CreateLimiter(IConfiguration configuration, string key, int defaultValue)
        {
            var configuredValue = configuration.GetValue<int?>(key) ?? defaultValue;
            var maxConcurrency = Math.Max(1, configuredValue);

            return new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        private sealed class ConcurrencyLease : IAsyncDisposable
        {
            private readonly SemaphoreSlim _limiter;
            private bool _disposed;

            public ConcurrencyLease(SemaphoreSlim limiter)
            {
                _limiter = limiter;
            }

            public ValueTask DisposeAsync()
            {
                if (!_disposed)
                {
                    _limiter.Release();
                    _disposed = true;
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
