using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Providers;
using Sms.DomainService.Repositories;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Services;

public class SmsProcessingService : ISmsProcessingService
{
    private readonly ISmsRepository _repository;
    private readonly ISmsProviderFactory _providerFactory;
    private readonly ISmsRetryPolicy _retryPolicy;
    private readonly ISmsEventPublisher _eventPublisher;
    private readonly ISmsOutboxService _outboxService;
    private readonly IMessageClient _messageClient;
    private readonly ILogger<SmsProcessingService> _logger;

    public SmsProcessingService(
        ISmsRepository repository,
        ISmsProviderFactory providerFactory,
        ISmsRetryPolicy retryPolicy,
        ISmsEventPublisher eventPublisher,
        ISmsOutboxService outboxService,
        IMessageClient messageClient,
        ILogger<SmsProcessingService> logger)
    {
        _repository = repository;
        _providerFactory = providerFactory;
        _retryPolicy = retryPolicy;
        _eventPublisher = eventPublisher;
        _outboxService = outboxService;
        _messageClient = messageClient;
        _logger = logger;
    }

    public async Task ProcessOutboxMessageAsync(ProcessSmsOutboxMessageCommand command, CancellationToken cancellationToken = default)
    {
        var outbox = await _repository.GetOutboxAsync(command.OutboxMessageId, cancellationToken, command.TenantId);
        if (outbox == null)
        {
            _logger.LogError("SmsProcessingService: missing SMS outbox OutboxMessageId={OutboxMessageId}, TenantId={TenantId}", command.OutboxMessageId, command.TenantId);
            return;
        }

        if (outbox.Status == SmsOutboxStatus.Completed)
        {
            _logger.LogInformation("SmsProcessingService: outbox already completed OutboxMessageId={OutboxMessageId}, MessageId={MessageId}", outbox.ItemId, outbox.MessageId);
            return;
        }

        if (outbox.Status == SmsOutboxStatus.Failed)
        {
            _logger.LogWarning("SmsProcessingService: outbox is failed and will not be processed OutboxMessageId={OutboxMessageId}, MessageId={MessageId}", outbox.ItemId, outbox.MessageId);
            return;
        }

        if (outbox.NextVisibleAt > DateTime.UtcNow)
        {
            _logger.LogInformation(
                "SmsProcessingService: outbox process command arrived before due time OutboxMessageId={OutboxMessageId}, MessageId={MessageId}, NextVisibleAt={NextVisibleAt}",
                outbox.ItemId,
                outbox.MessageId,
                outbox.NextVisibleAt);
            return;
        }

        var claimed = await _repository.TryClaimOutboxAsync(outbox.ItemId, DateTime.UtcNow, cancellationToken, outbox.TenantId);
        if (!claimed)
        {
            _logger.LogInformation("SmsProcessingService: outbox could not be claimed OutboxMessageId={OutboxMessageId}, MessageId={MessageId}", outbox.ItemId, outbox.MessageId);
            return;
        }

        try
        {
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<SendSmsCommand>
            {
                ConsumerName = SmsConstants.SmsSendQueue,
                Payload = new SendSmsCommand
                {
                    MessageId = outbox.MessageId,
                    OutboxMessageId = outbox.ItemId,
                    TenantId = outbox.TenantId,
                    ProjectKey = outbox.ProjectKey,
                    CorrelationId = outbox.CorrelationId
                }
            });

            _logger.LogInformation(
                "SmsProcessingService: published SMS send command OutboxMessageId={OutboxMessageId}, MessageId={MessageId}, TenantId={TenantId}, RetryCount={RetryCount}",
                outbox.ItemId,
                outbox.MessageId,
                outbox.TenantId,
                outbox.RetryCount);
        }
        catch (Exception ex)
        {
            var retryCount = outbox.RetryCount + 1;
            var canRetry = retryCount <= outbox.MaxRetryCount;
            var nextRetryAt = canRetry ? _retryPolicy.GetNextRetryAt(retryCount, DateTime.UtcNow) : DateTime.UtcNow;
            var status = canRetry ? SmsOutboxStatus.RetryScheduled : SmsOutboxStatus.Failed;

            await _repository.UpdateOutboxStatusAsync(outbox.ItemId, status, retryCount, nextRetryAt, ex.Message, cancellationToken, outbox.TenantId);

            if (canRetry)
            {
                outbox.RetryCount = retryCount;
                outbox.Status = status;
                outbox.NextVisibleAt = nextRetryAt;
                await _outboxService.RequestProcessAsync(outbox, cancellationToken);
            }

            _logger.LogError(
                ex,
                "SmsProcessingService: failed to publish SMS send command OutboxMessageId={OutboxMessageId}, MessageId={MessageId}, RetryCount={RetryCount}, Status={Status}",
                outbox.ItemId,
                outbox.MessageId,
                retryCount,
                status);
        }
    }

    public async Task ProcessCommandAsync(SendSmsCommand command, CancellationToken cancellationToken = default)
    {
        var message = await _repository.GetMessageAsync(command.MessageId, cancellationToken, command.TenantId);
        if (message == null)
        {
            _logger.LogError("SmsProcessingService: missing queued SMS MessageId={MessageId}", command.MessageId);
            return;
        }

        if (message.Status is SmsMessageStatus.Submitted or SmsMessageStatus.Delivered)
        {
            _logger.LogInformation("SmsProcessingService: duplicate send command ignored for MessageId={MessageId}, Status={Status}", message.ItemId, message.Status);
            return;
        }

        await SendWithProviderAsync(message, cancellationToken, command.TenantId, command.OutboxMessageId);
    }

    public async Task ProcessDueRetriesAsync(string tenantId, TimeSpan queueRecoveryGracePeriod, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        var lastQueuedBeforeUtc = utcNow.Subtract(queueRecoveryGracePeriod);
        var dueOutboxMessages = await _repository.GetStaleDueOutboxMessagesAsync(utcNow, lastQueuedBeforeUtc, 25, cancellationToken, tenantId);
        foreach (var outbox in dueOutboxMessages)
        {
            await ProcessRetryAsync(outbox, cancellationToken);
        }
    }

    private async Task ProcessRetryAsync(SmsOutboxMessage outbox, CancellationToken cancellationToken)
    {
        if (outbox.Status is not (SmsOutboxStatus.Pending or SmsOutboxStatus.RetryScheduled) || outbox.NextVisibleAt > DateTime.UtcNow)
        {
            return;
        }

        try
        {
            await _outboxService.RequestProcessAsync(outbox, cancellationToken);
        }
        catch (Exception ex)
        {
            await _repository.UpdateOutboxStatusAsync(outbox.ItemId, SmsOutboxStatus.RetryScheduled, outbox.RetryCount, outbox.NextVisibleAt, ex.Message, cancellationToken, outbox.TenantId);
            _logger.LogError(ex, "SmsProcessingService: failed to request due outbox processing OutboxMessageId={OutboxMessageId}, MessageId={MessageId}", outbox.ItemId, outbox.MessageId);
        }
    }

    public Task ReconcileDeliveryAsync(SmsDeliveryCheckEvent deliveryCheckEvent, CancellationToken cancellationToken = default)
    {
        return ReconcileDeliveryAsync(deliveryCheckEvent, cancellationToken, tenantId: null);
    }

    private async Task ReconcileDeliveryAsync(SmsDeliveryCheckEvent deliveryCheckEvent, CancellationToken cancellationToken, string? tenantId)
    {
        if (deliveryCheckEvent.NotBeforeUtc > DateTime.UtcNow)
        {
            _logger.LogInformation(
                "SmsProcessingService: delivery check arrived before due time MessageId={MessageId}, NotBeforeUtc={NotBeforeUtc}",
                deliveryCheckEvent.MessageId,
                deliveryCheckEvent.NotBeforeUtc);
            return;
        }

        var message = await _repository.GetMessageAsync(deliveryCheckEvent.MessageId, cancellationToken, tenantId);
        if (message == null || string.IsNullOrWhiteSpace(message.ProviderMessageId))
        {
            return;
        }

        if (message.Status is SmsMessageStatus.Delivered or SmsMessageStatus.Undelivered or SmsMessageStatus.DeliveryFailed)
        {
            return;
        }

        var configuration = await _repository.GetActiveProviderConfigurationAsync(cancellationToken, tenantId);
        if (configuration == null)
        {
            return;
        }

        var provider = _providerFactory.GetProvider(configuration);
        var delivery = await provider.GetDeliveryStatusAsync(message, configuration, cancellationToken);
        if (!delivery.IsFinal)
        {
            return;
        }

        await _repository.UpdateMessageStatusAsync(message.ItemId, delivery.Status, errorCode: delivery.ErrorCode, errorMessage: delivery.ErrorMessage, cancellationToken: cancellationToken, tenantId: tenantId);
        await PublishTerminalEventAsync(message, delivery.Status, delivery.ErrorCode, cancellationToken);
    }

    public async Task ReconcileSubmittedMessagesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var oldSubmittedMessages = await _repository.GetSubmittedMessagesOlderThanAsync(DateTime.UtcNow.AddMinutes(-10), 50, cancellationToken, tenantId);
        foreach (var message in oldSubmittedMessages)
        {
            await ReconcileDeliveryAsync(new SmsDeliveryCheckEvent
            {
                MessageId = message.ItemId,
                TenantId = message.TenantId,
                ProjectKey = message.ProjectKey,
                CorrelationId = message.CorrelationId,
                ProviderMessageId = message.ProviderMessageId ?? string.Empty
            }, cancellationToken, tenantId);
        }
    }

    private async Task SendWithProviderAsync(SmsMessage message, CancellationToken cancellationToken, string? tenantId = null, string? outboxMessageId = null)
    {
        var outbox = !string.IsNullOrWhiteSpace(outboxMessageId)
            ? await _repository.GetOutboxAsync(outboxMessageId, cancellationToken, tenantId)
            : await _repository.GetOutboxByMessageIdAsync(message.ItemId, cancellationToken, tenantId);
        var configuration = await _repository.GetActiveProviderConfigurationAsync(cancellationToken, tenantId);
        if (configuration == null)
        {
            await FailMessageAsync(message, outbox, "sms_provider_configuration_missing", "No active SMS provider configuration was found.", cancellationToken, tenantId);
            return;
        }

        var provider = _providerFactory.GetProvider(configuration);
        await _repository.UpdateMessageStatusAsync(message.ItemId, SmsMessageStatus.Processing, cancellationToken: cancellationToken, tenantId: tenantId);
        await _repository.IncrementMessageAttemptAsync(message.ItemId, cancellationToken, tenantId);

        var attempt = new SmsDeliveryAttempt
        {
            MessageId = message.ItemId,
            TenantId = message.TenantId,
            ProjectKey = message.ProjectKey,
            ProviderType = configuration.ProviderType,
            AttemptNumber = message.AttemptCount + 1,
            Status = SmsMessageStatus.Processing
        };

        var result = await provider.SendAsync(message, configuration, cancellationToken);
        attempt.CompletedAt = DateTime.UtcNow;
        attempt.ProviderMessageId = result.ProviderMessageId;

        if (result.IsSuccess)
        {
            attempt.Status = SmsMessageStatus.Submitted;
            await _repository.SaveAttemptAsync(attempt, cancellationToken);
            await _repository.UpdateMessageStatusAsync(message.ItemId, SmsMessageStatus.Submitted, result.ProviderMessageId, cancellationToken: cancellationToken, tenantId: tenantId);
            if (outbox != null)
            {
                await _repository.UpdateOutboxStatusAsync(outbox.ItemId, SmsOutboxStatus.Completed, cancellationToken: cancellationToken, tenantId: tenantId);
            }

            await PublishStatusAsync(message, SmsMessageStatus.Submitted, configuration.ProviderType, result.ProviderMessageId, null, cancellationToken);

            var deliveryCheckAt = DateTime.UtcNow.AddMinutes(Math.Max(1, configuration.DeliveryCheckDelayMinutes));
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<SmsDeliveryCheckEvent>
            {
                ConsumerName = SmsConstants.SmsDeliveryCheckQueue,
                ScheduledEnqueueTimeUtc = new DateTimeOffset(DateTime.SpecifyKind(deliveryCheckAt, DateTimeKind.Utc)),
                Payload = new SmsDeliveryCheckEvent
                {
                    MessageId = message.ItemId,
                    TenantId = message.TenantId,
                    ProjectKey = message.ProjectKey,
                    CorrelationId = message.CorrelationId,
                    ProviderMessageId = result.ProviderMessageId ?? string.Empty,
                    NotBeforeUtc = deliveryCheckAt
                }
            });

            _logger.LogInformation("SmsProcessingService: submitted MessageId={MessageId}, Provider={Provider}, CorrelationId={CorrelationId}", message.ItemId, configuration.ProviderType, message.CorrelationId);
            return;
        }

        attempt.Status = result.IsTransientFailure ? SmsMessageStatus.Queued : SmsMessageStatus.Failed;
        attempt.ErrorCode = result.ErrorCode;
        attempt.ErrorMessage = result.ErrorMessage;
        await _repository.SaveAttemptAsync(attempt, cancellationToken);

        if (result.IsTransientFailure && outbox != null && outbox.RetryCount < outbox.MaxRetryCount)
        {
            var retryCount = outbox.RetryCount + 1;
            var nextRetryAt = _retryPolicy.GetNextRetryAt(retryCount, DateTime.UtcNow);
            await _repository.UpdateOutboxStatusAsync(outbox.ItemId, SmsOutboxStatus.Failed, retryCount, nextRetryAt, result.ErrorMessage, cancellationToken, tenantId);
            await _repository.UpdateMessageStatusAsync(message.ItemId, SmsMessageStatus.Queued, errorCode: result.ErrorCode, errorMessage: result.ErrorMessage, cancellationToken: cancellationToken, tenantId: tenantId);

            var retryOutbox = _outboxService.CreateSendMessage(message, outbox.MaxRetryCount, retryCount, nextRetryAt);
            await _repository.SaveOutboxAsync(retryOutbox, cancellationToken);

            try
            {
                await _outboxService.RequestProcessAsync(retryOutbox, cancellationToken);
            }
            catch (Exception ex)
            {
                await _repository.UpdateOutboxStatusAsync(retryOutbox.ItemId, SmsOutboxStatus.RetryScheduled, retryCount, nextRetryAt, ex.Message, cancellationToken, tenantId);
                _logger.LogError(ex, "SmsProcessingService: failed to request scheduled retry processing MessageId={MessageId}, OutboxMessageId={OutboxMessageId}", message.ItemId, retryOutbox.ItemId);
            }

            _logger.LogWarning("SmsProcessingService: retry scheduled MessageId={MessageId}, RetryCount={RetryCount}, NextRetryAt={NextRetryAt}, OutboxMessageId={OutboxMessageId}", message.ItemId, retryCount, nextRetryAt, retryOutbox.ItemId);
            return;
        }

        await FailMessageAsync(message, outbox, result.ErrorCode ?? "sms_send_failed", result.ErrorMessage ?? "SMS provider send failed.", cancellationToken, tenantId);
    }

    private async Task FailMessageAsync(SmsMessage message, SmsOutboxMessage? outbox, string errorCode, string errorMessage, CancellationToken cancellationToken, string? tenantId)
    {
        await _repository.UpdateMessageStatusAsync(message.ItemId, SmsMessageStatus.Failed, errorCode: errorCode, errorMessage: errorMessage, cancellationToken: cancellationToken, tenantId: tenantId);
        if (outbox != null)
        {
            await _repository.UpdateOutboxStatusAsync(outbox.ItemId, SmsOutboxStatus.Failed, lastError: errorMessage, cancellationToken: cancellationToken, tenantId: tenantId);
        }

        await PublishStatusAsync(message, SmsMessageStatus.Failed, message.ProviderType, message.ProviderMessageId, errorCode, cancellationToken);

        _logger.LogError("SmsProcessingService: failed MessageId={MessageId}, ErrorCode={ErrorCode}, CorrelationId={CorrelationId}", message.ItemId, errorCode, message.CorrelationId);
    }

    private Task PublishTerminalEventAsync(SmsMessage message, SmsMessageStatus status, string? errorCode, CancellationToken cancellationToken)
    {
        return PublishStatusAsync(message, status, message.ProviderType, message.ProviderMessageId, errorCode, cancellationToken);
    }

    private Task PublishStatusAsync(SmsMessage message, SmsMessageStatus status, SmsProviderType? provider, string? providerMessageId, string? errorCode, CancellationToken cancellationToken)
    {
        return _eventPublisher.PublishStatusAsync(new SmsStatusEvent
        {
            MessageId = message.ItemId,
            TenantId = message.TenantId,
            ProjectKey = message.ProjectKey,
            CorrelationId = message.CorrelationId,
            Provider = provider,
            ProviderMessageId = providerMessageId,
            Status = status,
            ErrorCode = errorCode
        }, cancellationToken);
    }
}
