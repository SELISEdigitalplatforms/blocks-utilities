using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Repositories;

public interface ISmsRepository
{
    Task SaveMessageAsync(SmsMessage message, CancellationToken cancellationToken = default);
    Task<SmsMessage?> GetMessageAsync(string messageId, CancellationToken cancellationToken = default, string? tenantId = null);
    Task<SmsMessage?> GetMessageByProviderMessageIdAsync(string providerMessageId, CancellationToken cancellationToken = default, string? tenantId = null);
    Task UpdateMessageStatusAsync(string messageId, SmsMessageStatus status, string? providerMessageId = null, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default, string? tenantId = null);
    Task IncrementMessageAttemptAsync(string messageId, CancellationToken cancellationToken = default, string? tenantId = null);
    Task SaveOutboxAsync(SmsOutboxMessage outbox, CancellationToken cancellationToken = default);
    Task<SmsOutboxMessage?> GetOutboxByMessageIdAsync(string messageId, CancellationToken cancellationToken = default, string? tenantId = null);
    Task<List<SmsOutboxMessage>> GetDueOutboxMessagesAsync(DateTime utcNow, int limit, CancellationToken cancellationToken = default, string? tenantId = null);
    Task UpdateOutboxStatusAsync(string outboxId, SmsOutboxStatus status, int? retryCount = null, DateTime? nextVisibleAt = null, string? lastError = null, CancellationToken cancellationToken = default, string? tenantId = null);
    Task SaveAttemptAsync(SmsDeliveryAttempt attempt, CancellationToken cancellationToken = default);
    Task SaveProviderConfigurationAsync(SmsProviderConfiguration configuration, CancellationToken cancellationToken = default);
    Task<SmsProviderConfiguration?> GetActiveProviderConfigurationAsync(CancellationToken cancellationToken = default, string? tenantId = null);
    Task<SmsTemplate?> GetTemplateAsync(string templateName, string language, CancellationToken cancellationToken = default, string? tenantId = null);
    Task<long> CountMessagesSinceAsync(DateTime sinceUtc, string? destinationNumber, CancellationToken cancellationToken = default, string? tenantId = null);
    Task<List<SmsMessage>> GetSubmittedMessagesOlderThanAsync(DateTime olderThanUtc, int limit, CancellationToken cancellationToken = default, string? tenantId = null);
}
