using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Repositories;

public interface ISmsRepository
{
    Task SaveMessageAsync(SmsMessage message, CancellationToken cancellationToken = default);
    Task<SmsMessage?> GetMessageAsync(string projectKey, string messageId, CancellationToken cancellationToken = default);
    Task<SmsMessage?> GetMessageByProviderMessageIdAsync(string projectKey, string providerMessageId, CancellationToken cancellationToken = default);
    Task UpdateMessageStatusAsync(string projectKey, string messageId, SmsMessageStatus status, string? providerMessageId = null, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task IncrementMessageAttemptAsync(string projectKey, string messageId, CancellationToken cancellationToken = default);
    Task SaveOutboxAsync(SmsOutboxMessage outbox, CancellationToken cancellationToken = default);
    Task<SmsOutboxMessage?> GetOutboxByMessageIdAsync(string projectKey, string messageId, CancellationToken cancellationToken = default);
    Task<List<SmsOutboxMessage>> GetDueOutboxMessagesAsync(DateTime utcNow, int limit, CancellationToken cancellationToken = default);
    Task UpdateOutboxStatusAsync(string projectKey, string outboxId, SmsOutboxStatus status, int? retryCount = null, DateTime? nextVisibleAt = null, string? lastError = null, CancellationToken cancellationToken = default);
    Task SaveAttemptAsync(SmsDeliveryAttempt attempt, CancellationToken cancellationToken = default);
    Task SaveProviderConfigurationAsync(SmsProviderConfiguration configuration, CancellationToken cancellationToken = default);
    Task<SmsProviderConfiguration?> GetActiveProviderConfigurationAsync(string projectKey, CancellationToken cancellationToken = default);
    Task<SmsTemplate?> GetTemplateAsync(string projectKey, string templateName, string language, CancellationToken cancellationToken = default);
    Task<long> CountMessagesSinceAsync(string projectKey, string tenantId, DateTime sinceUtc, string? destinationNumber, CancellationToken cancellationToken = default);
    Task<List<SmsMessage>> GetSubmittedMessagesOlderThanAsync(DateTime olderThanUtc, int limit, CancellationToken cancellationToken = default);
}
