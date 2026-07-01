using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public interface IMailOutboxService
    {
        MailOutboxMessage CreateMessage<T>(string aggregateId, string destination, T payload, string deduplicationKey, DateTime? nextAttemptUtc = null)
            where T : class;

        Task EnqueueAsync<T>(string aggregateId, string destination, T payload, string deduplicationKey, DateTime? nextAttemptUtc = null)
            where T : class;

        Task<int> PublishPendingAsync(CancellationToken cancellationToken = default);
    }
}
