using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails.Services.Outbox
{
    public interface IMailOutboxService
    {
        MailOutboxMessage CreateMessage<T>(string aggregateId, string destination, T payload, string deduplicationKey, DateTime? nextAttemptUtc = null)
            where T : class;

        Task EnqueueAsync<T>(string aggregateId, string destination, T payload, string deduplicationKey, DateTime? nextAttemptUtc = null)
            where T : class;

        Task RequestProcessAsync(MailOutboxMessage outboxMessage);

        Task<bool> ProcessOutboxMessageAsync(string tenantId, string outboxMessageId, CancellationToken cancellationToken = default);

        Task<int> PublishPendingAsync(CancellationToken cancellationToken = default);

        Task<int> PublishPendingAsync(string tenantId, CancellationToken cancellationToken = default);
    }
}
