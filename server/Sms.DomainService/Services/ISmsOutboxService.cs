using Sms.DomainService.Entities;

namespace Sms.DomainService.Services;

public interface ISmsOutboxService
{
    SmsOutboxMessage CreateSendMessage(SmsMessage message, int maxRetryCount, int retryCount = 0, DateTime? nextVisibleAt = null);
    Task RequestProcessAsync(SmsOutboxMessage outboxMessage, CancellationToken cancellationToken = default);
}
