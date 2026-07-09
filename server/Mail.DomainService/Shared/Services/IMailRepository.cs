using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Services
{
    public interface IMailRepository
    {
        Task<bool> FileExists(string fileId);
        Task<List<string>> GetEmailAdressOfUsers(IEnumerable<string> emails);
        Task<MailServerConfiguration> GetMailServerConfigurationByTenantId(string tenantId);
        Task<EmailTemplate> GetEmailTemplateByPurpose(string purpose, string language, string organizationId);
        Task<MailServerConfiguration> GetMailServerConfigurationByPurpose(string purpose, string language, string organizationId);
        Task<bool> MailTemplateForPurposeExists(string purpose, string language);
        Task<bool> MailServerConfigurationExists(string purpose, string language);
        Task<bool> SaveMailToBeSent(MailToBeSent mailToBeSent);
        Task<bool> SaveMailToBeSentWithOutboxAsync(MailToBeSent mailToBeSent, MailOutboxMessage outboxMessage);
        Task<MailToBeSent> GetMailToBeSent(string itemId);
        Task<MailToBeSent> GetMailToBeSent(string tenantId, string itemId);
        Task<bool> TryStartMailSubmissionAsync(string itemId, DateTime startedAtUtc, int processingLockTimeoutMinutes);
        Task<bool> TryStartMailSubmissionAsync(string tenantId, string itemId, DateTime startedAtUtc, int processingLockTimeoutMinutes);
        Task UpdateMailSubmissionAcceptedAsync(string itemId, string internetMessageId, DateTime submittedAtUtc, string senderAddress, IEnumerable<MailRecipientDeliveryStatus> recipientStatuses, MailSubmissionResult submissionResult);
        Task UpdateMailSubmissionAcceptedAsync(string tenantId, string itemId, string internetMessageId, DateTime submittedAtUtc, string senderAddress, IEnumerable<MailRecipientDeliveryStatus> recipientStatuses, MailSubmissionResult submissionResult);
        Task UpdateMailSubmissionFailedAsync(string itemId, MailSubmissionStatus status, MailSubmissionResult submissionResult);
        Task UpdateMailSubmissionFailedAsync(string tenantId, string itemId, MailSubmissionStatus status, MailSubmissionResult submissionResult);
        Task UpdateMailSubmissionTrackingAsync(string itemId, string internetMessageId, DateTime submittedAtUtc, string senderAddress, IEnumerable<MailRecipientDeliveryStatus> recipientStatuses);
        Task UpdateMailSubmissionTrackingAsync(string tenantId, string itemId, string internetMessageId, DateTime submittedAtUtc, string senderAddress, IEnumerable<MailRecipientDeliveryStatus> recipientStatuses);
        Task UpdateMailRecipientDeliveryStatusAsync(string itemId, string recipient, MailStatus status, string? statusReason, DateTime checkedAtUtc);
        Task UpdateMailRecipientDeliveryStatusAsync(string tenantId, string itemId, string recipient, MailStatus status, string? statusReason, DateTime checkedAtUtc);
        Task<bool> TryClaimSesNotificationAsync(string tenantId, string messageId, string mailItemId, string eventType, DateTime claimedAtUtc);
        Task MarkSesNotificationProcessedAsync(string tenantId, string messageId, string? providerMessageId, string mailItemId, DateTime processedAtUtc);
        Task ReleaseSesNotificationAsync(string tenantId, string messageId, string lastError);
        Task InsertOutboxMessageAsync(MailOutboxMessage outboxMessage);
        Task InsertOutboxMessageAsync(string tenantId, MailOutboxMessage outboxMessage);
        Task<MailOutboxMessage> GetOutboxMessageAsync(string tenantId, string itemId);
        Task<IReadOnlyList<MailOutboxMessage>> GetPendingOutboxMessagesAsync(DateTime utcNow, int batchSize);
        Task<IReadOnlyList<MailOutboxMessage>> GetPendingOutboxMessagesAsync(string tenantId, DateTime utcNow, int batchSize);
        Task<bool> TryClaimOutboxMessageAsync(string itemId, DateTime claimedAtUtc);
        Task<bool> TryClaimOutboxMessageAsync(string tenantId, string itemId, DateTime claimedAtUtc);
        Task MarkOutboxMessagePublishedAsync(string itemId, DateTime publishedAtUtc);
        Task MarkOutboxMessagePublishedAsync(string tenantId, string itemId, DateTime publishedAtUtc);
        Task MarkOutboxMessageFailedAsync(string itemId, int attemptCount, DateTime nextAttemptUtc, OutboxMessageStatus status, string lastError);
        Task MarkOutboxMessageFailedAsync(string tenantId, string itemId, int attemptCount, DateTime nextAttemptUtc, OutboxMessageStatus status, string lastError);
        Task<EmailSendQueryResult> GetEmailSendsAsync(GetEmailSends request, string tenantId);
        Task<(List<MailBoxEntity> Mails, long TotalCount)> GetMailBoxMails(GetMailBoxMails request);
        Task<(List<MailBoxEntityResponse> Mails, long TotalCount)> GetMailBoxAggregatedMails(GetMailBoxMails request);
        Task<MailBoxEntity> GetMailBoxMail(string messageId, string projectKey);
    }
}
