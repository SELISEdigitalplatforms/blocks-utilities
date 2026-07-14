using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Repositories;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Services;

public class SmsOutboxService : ISmsOutboxService
{
    private readonly IMessageClient _messageClient;
    private readonly ISmsRepository _repository;
    private readonly ILogger<SmsOutboxService> _logger;

    public SmsOutboxService(IMessageClient messageClient, ISmsRepository repository, ILogger<SmsOutboxService> logger)
    {
        _messageClient = messageClient;
        _repository = repository;
        _logger = logger;
    }

    public SmsOutboxMessage CreateSendMessage(SmsMessage message, int maxRetryCount, int retryCount = 0, DateTime? nextVisibleAt = null)
    {
        var nextAttemptAt = nextVisibleAt ?? DateTime.UtcNow;

        return new SmsOutboxMessage
        {
            MessageId = message.ItemId,
            TenantId = message.TenantId,
            ProjectKey = message.ProjectKey,
            CorrelationId = message.CorrelationId,
            RetryCount = retryCount,
            MaxRetryCount = maxRetryCount,
            NextVisibleAt = nextAttemptAt,
            CreatedDate = DateTime.UtcNow,
            LastUpdatedDate = DateTime.UtcNow
        };
    }

    public async Task RequestProcessAsync(SmsOutboxMessage outboxMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outboxMessage);

        var notBeforeUtc = DateTime.SpecifyKind(outboxMessage.NextVisibleAt, DateTimeKind.Utc);
        await _messageClient.SendToConsumerAsync(new ConsumerMessage<ProcessSmsOutboxMessageCommand>
        {
            ConsumerName = SmsConstants.SmsOutboxProcessQueue,
            ScheduledEnqueueTimeUtc = new DateTimeOffset(notBeforeUtc),
            Payload = new ProcessSmsOutboxMessageCommand
            {
                OutboxMessageId = outboxMessage.ItemId,
                TenantId = outboxMessage.TenantId,
                ProjectKey = outboxMessage.ProjectKey,
                CorrelationId = outboxMessage.CorrelationId,
                NotBeforeUtc = notBeforeUtc
            }
        });

        await _repository.MarkOutboxQueuedAsync(outboxMessage.ItemId, DateTime.UtcNow, cancellationToken, outboxMessage.TenantId);

        _logger.LogInformation(
            "SmsOutboxService: requested outbox processing OutboxMessageId={OutboxMessageId}, MessageId={MessageId}, TenantId={TenantId}, NotBeforeUtc={NotBeforeUtc}, RetryCount={RetryCount}",
            outboxMessage.ItemId,
            outboxMessage.MessageId,
            outboxMessage.TenantId,
            notBeforeUtc,
            outboxMessage.RetryCount);
    }
}
