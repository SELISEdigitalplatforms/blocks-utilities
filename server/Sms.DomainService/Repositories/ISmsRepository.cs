using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Repositories;

public interface ISmsRepository
{
    Task SaveMessageAsync(SmsMessage message, CancellationToken cancellationToken = default);
    Task<SmsMessage?> GetMessageAsync(string tenantId, string messageId, CancellationToken cancellationToken = default);

    /// <summary>Accepted → Queued, and nothing else: a worker may already have claimed it.</summary>
    Task MarkQueuedAsync(string tenantId, string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically takes the send lease. Null when another worker holds a live lease or the message
    /// is in no state to send.
    /// </summary>
    Task<SmsMessage?> TryClaimForSendAsync(string tenantId, string messageId, string leaseId, DateTime utcNow, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Writes one recipient's outcome under the caller's lease, right after the provider call.</summary>
    Task UpdateRecipientAsync(string tenantId, string messageId, string leaseId, SmsRecipient recipient, CancellationToken cancellationToken = default);

    /// <summary>Ends a send round: sets the message status and releases the lease.</summary>
    Task CompleteSendRoundAsync(string tenantId, string messageId, string leaseId, SmsMessageStatus status, string? errorCode, string? errorMessage, CancellationToken cancellationToken = default);

    Task SetStatusAsync(string tenantId, string messageId, SmsMessageStatus status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task IncrementDeliveryCheckAsync(string tenantId, string messageId, CancellationToken cancellationToken = default);
    Task<SmsMessage?> GetMessageByProviderMessageIdAsync(string tenantId, string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a delivery outcome for the recipient holding <paramref name="providerMessageId"/>,
    /// only while it is still Submitted. False when it was already final (a duplicate callback).
    /// </summary>
    Task<bool> ApplyRecipientDeliveryAsync(string tenantId, string messageId, string providerMessageId, SmsRecipientStatus status, string? errorCode, string? errorMessage, CancellationToken cancellationToken = default);

    Task SaveAttemptAsync(SmsDeliveryAttempt attempt, CancellationToken cancellationToken = default);

    Task SaveProviderConfigurationAsync(SmsProviderConfiguration configuration, CancellationToken cancellationToken = default);
    Task<SmsProviderConfiguration?> GetProviderConfigurationAsync(string tenantId, string configurationId, CancellationToken cancellationToken = default);
    /// <summary>The enabled configuration to use, default first; limited to one provider when given.</summary>
    Task<SmsProviderConfiguration?> GetActiveProviderConfigurationAsync(string tenantId, SmsProviderType? providerType = null, CancellationToken cancellationToken = default);
    Task ClearOtherDefaultsAsync(string tenantId, string keepConfigurationId, CancellationToken cancellationToken = default);

    Task<SmsTemplate?> GetTemplateAsync(string tenantId, string templateName, string language, CancellationToken cancellationToken = default);
    Task<SmsTemplate?> GetTemplateByIdAsync(string tenantId, string templateId, CancellationToken cancellationToken = default);
    Task<(List<SmsTemplate> Items, long TotalCount)> ListTemplatesAsync(string tenantId, string? search, string? language, int skip, int take, CancellationToken cancellationToken = default);
    Task SaveTemplateAsync(SmsTemplate template, CancellationToken cancellationToken = default);
    Task<bool> DeleteTemplateAsync(string tenantId, string templateId, CancellationToken cancellationToken = default);
}
