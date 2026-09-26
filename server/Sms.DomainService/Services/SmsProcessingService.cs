using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Providers;
using Sms.DomainService.Repositories;
using Sms.DomainService.Scheduling;

namespace Sms.DomainService.Services;

public class SmsProcessingService : ISmsProcessingService
{
    private static readonly TimeSpan SendLease = TimeSpan.FromMinutes(5);

    // ponytail: fixed; after this many polls a still-Submitted recipient is left to the provider callback.
    private const int MaxDeliveryChecks = 6;

    private readonly ISmsRepository _repository;
    private readonly ISmsWorkQueue _workQueue;
    private readonly ISmsProviderFactory _providerFactory;
    private readonly ISmsProviderContextResolver _contextResolver;
    private readonly ISmsRetryPolicy _retryPolicy;
    private readonly ISmsEventPublisher _eventPublisher;
    private readonly TimeProvider _time;
    private readonly ILogger<SmsProcessingService> _logger;

    public SmsProcessingService(
        ISmsRepository repository,
        ISmsWorkQueue workQueue,
        ISmsProviderFactory providerFactory,
        ISmsProviderContextResolver contextResolver,
        ISmsRetryPolicy retryPolicy,
        ISmsEventPublisher eventPublisher,
        ILogger<SmsProcessingService> logger,
        TimeProvider? time = null)
    {
        _repository = repository;
        _workQueue = workQueue;
        _providerFactory = providerFactory;
        _contextResolver = contextResolver;
        _retryPolicy = retryPolicy;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task ProcessSendAsync(string tenantId, string messageId, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        // Watchdog first, before anything can go wrong: if this worker dies or throws after taking
        // the lease, this item reclaims the message once the lease lapses. Without it a redelivered
        // command would find the lease still live, do nothing, and the message would sit in
        // Processing for good.
        await _workQueue.ScheduleAsync(tenantId, messageId, string.Empty, SmsWorkKind.Retry, now.Add(SendLease).AddMinutes(1), cancellationToken);

        var leaseId = Guid.NewGuid().ToString("N");
        var message = await _repository.TryClaimForSendAsync(tenantId, messageId, leaseId, now, SendLease, cancellationToken);
        if (message == null)
        {
            _logger.LogInformation("SmsProcessingService: nothing to send MessageId={MessageId} (in flight elsewhere, already sent, or missing)", messageId);
            return;
        }

        var configuration = await _repository.GetActiveProviderConfigurationAsync(tenantId, message.ProviderType, cancellationToken);
        if (configuration == null)
        {
            await FailAllPendingAsync(message, leaseId, "sms_provider_configuration_missing", "No active SMS provider configuration was found.", cancellationToken);
            return;
        }

        var context = await _contextResolver.ResolveAsync(tenantId, configuration, cancellationToken);
        var provider = _providerFactory.GetProvider(configuration);
        var submittedThisRound = false;

        // Only recipients still pending: a retry never goes back to a number that already has an
        // answer, and each outcome is written as soon as it is known so a crash mid-loop cannot
        // lose it.
        foreach (var recipient in message.Recipients.Where(r => r.Status == SmsRecipientStatus.Pending))
        {
            recipient.Attempts++;
            var result = await provider.SendAsync(context, recipient.Number, message.MessageText, $"{message.ItemId}:{recipient.Number}:{recipient.Attempts}", cancellationToken);

            if (result.IsSuccess)
            {
                recipient.Status = SmsRecipientStatus.Submitted;
                recipient.ProviderMessageId = result.ProviderMessageId;
                recipient.ErrorCode = null;
                recipient.ErrorMessage = null;
                submittedThisRound = true;
            }
            else
            {
                recipient.Status = result.IsTransientFailure ? SmsRecipientStatus.Pending : SmsRecipientStatus.Failed;
                recipient.ErrorCode = result.ErrorCode;
                recipient.ErrorMessage = result.ErrorMessage;
            }

            recipient.LastUpdatedDate = _time.GetUtcNow().UtcDateTime;
            await _repository.UpdateRecipientAsync(tenantId, message.ItemId, leaseId, recipient, cancellationToken);
            await _repository.SaveAttemptAsync(new SmsDeliveryAttempt
            {
                MessageId = message.ItemId,
                TenantId = tenantId,
                ProviderType = configuration.ProviderType,
                RecipientNumber = recipient.Number,
                ProviderMessageId = result.ProviderMessageId,
                AttemptNumber = recipient.Attempts,
                Status = result.IsSuccess ? SmsMessageStatus.Submitted : result.IsTransientFailure ? SmsMessageStatus.RetryScheduled : SmsMessageStatus.Failed,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                CompletedAt = recipient.LastUpdatedDate
            }, cancellationToken);
        }

        if (submittedThisRound)
        {
            await _workQueue.ScheduleAsync(tenantId, message.ItemId, message.CorrelationId, SmsWorkKind.DeliveryCheck,
                _time.GetUtcNow().UtcDateTime.AddMinutes(configuration.DeliveryCheckDelayMinutes), cancellationToken);
        }

        var pending = message.Recipients.Where(r => r.Status == SmsRecipientStatus.Pending).ToList();
        var lastError = pending.Concat(message.Recipients).FirstOrDefault(r => r.ErrorCode != null);

        if (pending.Count > 0 && message.AttemptCount < configuration.MaxRetryAttempts)
        {
            var retryAt = _retryPolicy.GetNextRetryAt(message.AttemptCount, _time.GetUtcNow().UtcDateTime);
            await _workQueue.ScheduleAsync(tenantId, message.ItemId, message.CorrelationId, SmsWorkKind.Retry, retryAt, cancellationToken);
            await _repository.CompleteSendRoundAsync(tenantId, message.ItemId, leaseId, SmsMessageStatus.RetryScheduled, lastError?.ErrorCode, lastError?.ErrorMessage, cancellationToken);
            _logger.LogWarning("SmsProcessingService: retry scheduled MessageId={MessageId}, Attempt={Attempt}, PendingRecipients={Pending}, RetryAt={RetryAt}",
                message.ItemId, message.AttemptCount, pending.Count, retryAt);
            return;
        }

        foreach (var recipient in pending)
        {
            recipient.Status = SmsRecipientStatus.Failed;
            await _repository.UpdateRecipientAsync(tenantId, message.ItemId, leaseId, recipient, cancellationToken);
        }

        var status = SmsStatusRollup.AfterSend(message.Recipients);
        await _repository.CompleteSendRoundAsync(tenantId, message.ItemId, leaseId, status, lastError?.ErrorCode, lastError?.ErrorMessage, cancellationToken);
        await _workQueue.CancelAsync(tenantId, message.ItemId, SmsWorkKind.Retry, cancellationToken);
        await PublishAsync(message, status, lastError?.ErrorCode, cancellationToken);
    }

    public async Task CheckDeliveryAsync(string tenantId, string messageId, CancellationToken cancellationToken = default)
    {
        var message = await _repository.GetMessageAsync(tenantId, messageId, cancellationToken);
        var outstanding = message?.Recipients
            .Where(r => r.Status == SmsRecipientStatus.Submitted && !string.IsNullOrWhiteSpace(r.ProviderMessageId))
            .ToList();
        if (message == null || outstanding is not { Count: > 0 })
        {
            return;
        }

        var configuration = await _repository.GetActiveProviderConfigurationAsync(tenantId, message.ProviderType, cancellationToken);
        if (configuration == null)
        {
            return;
        }

        var context = await _contextResolver.ResolveAsync(tenantId, configuration, cancellationToken);
        var provider = _providerFactory.GetProvider(configuration);

        foreach (var recipient in outstanding)
        {
            var delivery = await provider.GetDeliveryStatusAsync(context, recipient.ProviderMessageId!, cancellationToken);
            if (delivery.FinalStatus is { } final)
            {
                await _repository.ApplyRecipientDeliveryAsync(tenantId, message.ItemId, recipient.ProviderMessageId!, final, delivery.ErrorCode, delivery.ErrorMessage, cancellationToken);
            }
        }

        if (!await RollUpDeliveryAsync(tenantId, message.ItemId, cancellationToken) && message.DeliveryCheckCount + 1 < MaxDeliveryChecks)
        {
            await _repository.IncrementDeliveryCheckAsync(tenantId, message.ItemId, cancellationToken);
            await _workQueue.ScheduleAsync(tenantId, message.ItemId, message.CorrelationId, SmsWorkKind.DeliveryCheck,
                _time.GetUtcNow().UtcDateTime.AddMinutes(configuration.DeliveryCheckDelayMinutes), cancellationToken);
        }
    }

    public async Task<bool> ApplyCallbackAsync(string tenantId, SmsDeliveryCallback callback, CancellationToken cancellationToken = default)
    {
        var message = await _repository.GetMessageByProviderMessageIdAsync(tenantId, callback.ProviderMessageId, cancellationToken);
        if (message == null)
        {
            return false;
        }

        // Intermediate states (queued, sent, ...) need no write; a repeated final one is a no-op.
        if (callback.FinalStatus is { } final &&
            await _repository.ApplyRecipientDeliveryAsync(tenantId, message.ItemId, callback.ProviderMessageId, final, callback.ErrorCode, callback.ErrorMessage, cancellationToken))
        {
            await RollUpDeliveryAsync(tenantId, message.ItemId, cancellationToken);
        }

        return true;
    }

    /// <summary>True once every recipient has a final outcome (and the message says so).</summary>
    private async Task<bool> RollUpDeliveryAsync(string tenantId, string messageId, CancellationToken cancellationToken)
    {
        var message = await _repository.GetMessageAsync(tenantId, messageId, cancellationToken);
        if (message == null)
        {
            return true;
        }

        var status = SmsStatusRollup.AfterDelivery(message.Recipients);
        if (status == null)
        {
            return false;
        }

        if (status != message.Status)
        {
            await _repository.SetStatusAsync(tenantId, messageId, status.Value, cancellationToken: cancellationToken);
            await _workQueue.CancelAsync(tenantId, messageId, SmsWorkKind.DeliveryCheck, cancellationToken);
            await PublishAsync(message, status.Value, null, cancellationToken);
        }

        return true;
    }

    private async Task FailAllPendingAsync(SmsMessage message, string leaseId, string errorCode, string errorMessage, CancellationToken cancellationToken)
    {
        foreach (var recipient in message.Recipients.Where(r => r.Status == SmsRecipientStatus.Pending))
        {
            recipient.Status = SmsRecipientStatus.Failed;
            recipient.ErrorCode = errorCode;
            recipient.ErrorMessage = errorMessage;
            await _repository.UpdateRecipientAsync(message.TenantId, message.ItemId, leaseId, recipient, cancellationToken);
        }

        var status = SmsStatusRollup.AfterSend(message.Recipients);
        await _repository.CompleteSendRoundAsync(message.TenantId, message.ItemId, leaseId, status, errorCode, errorMessage, cancellationToken);
        await _workQueue.CancelAsync(message.TenantId, message.ItemId, SmsWorkKind.Retry, cancellationToken);
        await PublishAsync(message, status, errorCode, cancellationToken);
        _logger.LogError("SmsProcessingService: failed MessageId={MessageId}, ErrorCode={ErrorCode}", message.ItemId, errorCode);
    }

    private Task PublishAsync(SmsMessage message, SmsMessageStatus status, string? errorCode, CancellationToken cancellationToken) =>
        _eventPublisher.PublishStatusAsync(new SmsStatusEvent
        {
            MessageId = message.ItemId,
            TenantId = message.TenantId,
            CorrelationId = message.CorrelationId,
            Provider = message.ProviderType,
            Status = status,
            ErrorCode = errorCode
        }, cancellationToken);
}
