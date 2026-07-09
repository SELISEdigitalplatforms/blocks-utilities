using System.Text.Json;
using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Services.Outbox
{
    public class MailOutboxService : IMailOutboxService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly ILogger<MailOutboxService> _logger;
        private readonly IMailRepository _mailRepository;
        private readonly IMessageClient _messageClient;
        private readonly IConfiguration _configuration;

        public MailOutboxService(
            ILogger<MailOutboxService> logger,
            IMailRepository mailRepository,
            IMessageClient messageClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _mailRepository = mailRepository;
            _messageClient = messageClient;
            _configuration = configuration;
        }

        public MailOutboxMessage CreateMessage<T>(string aggregateId, string destination, T payload, string deduplicationKey, DateTime? nextAttemptUtc = null)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(payload);

            return new MailOutboxMessage
            {
                ItemId = Guid.NewGuid().ToString(),
                AggregateId = aggregateId,
                MessageType = typeof(T).Name,
                Destination = destination,
                PayloadJson = JsonSerializer.Serialize(payload, SerializerOptions),
                DeduplicationKey = deduplicationKey,
                Status = OutboxMessageStatus.Pending,
                AttemptCount = 0,
                CreatedAtUtc = DateTime.UtcNow,
                NextAttemptUtc = nextAttemptUtc ?? DateTime.UtcNow,
                ProjectKey = TryGetProperty(payload, nameof(MailToBeSent.ProjectKey)),
                TenantId = TryGetProperty(payload, nameof(MailToBeSent.TenantId)),
                OrganizationId = TryGetProperty(payload, nameof(MailToBeSent.OrganizationId))
            };
        }

        public async Task EnqueueAsync<T>(string aggregateId, string destination, T payload, string deduplicationKey, DateTime? nextAttemptUtc = null)
            where T : class
        {
            var message = CreateMessage(aggregateId, destination, payload, deduplicationKey, nextAttemptUtc);
            await _mailRepository.InsertOutboxMessageAsync(message.TenantId ?? string.Empty, message);
            await RequestProcessAsync(message);
        }

        public async Task RequestProcessAsync(MailOutboxMessage outboxMessage)
        {
            if (outboxMessage == null)
            {
                return;
            }

            try
            {
                await _messageClient.SendToConsumerAsync(new ConsumerMessage<ProcessMailOutboxMessageCommand>
                {
                    ConsumerName = CommunicationConstants.MailOutboxProcessQueueName,
                    Payload = new ProcessMailOutboxMessageCommand
                    {
                        OutboxMessageId = outboxMessage.ItemId,
                        TenantId = outboxMessage.TenantId ?? string.Empty,
                        ProjectKey = outboxMessage.ProjectKey ?? string.Empty,
                        OrganizationId = outboxMessage.OrganizationId ?? string.Empty,
                        NotBeforeUtc = outboxMessage.NextAttemptUtc
                    }
                });

                _logger.LogInformation(
                    "Queued mail outbox process request. OutboxMessageId={OutboxMessageId}, AggregateId={AggregateId}, MessageType={MessageType}, TenantId={TenantId}, ProjectKey={ProjectKey}, OrganizationId={OrganizationId}",
                    outboxMessage.ItemId,
                    outboxMessage.AggregateId,
                    outboxMessage.MessageType,
                    outboxMessage.TenantId,
                    outboxMessage.ProjectKey,
                    outboxMessage.OrganizationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to queue mail outbox process request. OutboxMessageId={OutboxMessageId}, AggregateId={AggregateId}, MessageType={MessageType}, TenantId={TenantId}, ProjectKey={ProjectKey}, OrganizationId={OrganizationId}",
                    outboxMessage.ItemId,
                    outboxMessage.AggregateId,
                    outboxMessage.MessageType,
                    outboxMessage.TenantId,
                    outboxMessage.ProjectKey,
                    outboxMessage.OrganizationId);
            }
        }

        public async Task<bool> ProcessOutboxMessageAsync(string tenantId, string outboxMessageId, CancellationToken cancellationToken = default)
        {
            if (!_configuration.GetValue("MailOutbox:Enabled", true))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(outboxMessageId))
            {
                return false;
            }

            var message = await _mailRepository.GetOutboxMessageAsync(tenantId, outboxMessageId);
            if (message == null)
            {
                _logger.LogError(
                    "Mail outbox process request could not be processed because outbox message was not found. OutboxMessageId={OutboxMessageId}, TenantId={TenantId}",
                    outboxMessageId,
                    tenantId);
                return false;
            }

            if (message.Status == OutboxMessageStatus.Published)
            {
                _logger.LogInformation(
                    "Skipping mail outbox process request because message is already published. OutboxMessageId={OutboxMessageId}, TenantId={TenantId}, ProjectKey={ProjectKey}, OrganizationId={OrganizationId}",
                    message.ItemId,
                    tenantId,
                    message.ProjectKey,
                    message.OrganizationId);
                return true;
            }

            if (message.Status == OutboxMessageStatus.DeadLettered)
            {
                _logger.LogWarning(
                    "Skipping mail outbox process request because message is dead-lettered. OutboxMessageId={OutboxMessageId}, TenantId={TenantId}, ProjectKey={ProjectKey}, OrganizationId={OrganizationId}",
                    message.ItemId,
                    tenantId,
                    message.ProjectKey,
                    message.OrganizationId);
                return false;
            }

            return await TryPublishMessageAsync(tenantId, message, cancellationToken);
        }

        public async Task<int> PublishPendingAsync(CancellationToken cancellationToken = default)
        {
            return await PublishPendingAsync(string.Empty, cancellationToken);
        }

        public async Task<int> PublishPendingAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            if (!_configuration.GetValue("MailOutbox:Enabled", true))
            {
                return 0;
            }

            var batchSize = Math.Max(1, _configuration.GetValue<int?>("MailOutbox:BatchSize") ?? 50);
            var pendingMessages = await _mailRepository.GetPendingOutboxMessagesAsync(tenantId, DateTime.UtcNow, batchSize);
            var publishedCount = 0;

            foreach (var message in pendingMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await TryPublishMessageAsync(tenantId, message, cancellationToken))
                {
                    publishedCount++;
                }
            }

            return publishedCount;
        }

        private async Task<bool> TryPublishMessageAsync(string tenantId, MailOutboxMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var messageTenantId = string.IsNullOrWhiteSpace(tenantId)
                ? string.Empty
                : message.TenantId ?? tenantId;

            var claimed = await _mailRepository.TryClaimOutboxMessageAsync(messageTenantId, message.ItemId, DateTime.UtcNow);
            if (!claimed)
            {
                return false;
            }

            try
            {
                await PublishAsync(message);
                await _mailRepository.MarkOutboxMessagePublishedAsync(messageTenantId, message.ItemId, DateTime.UtcNow);

                _logger.LogInformation(
                    "Published mail outbox message. OutboxMessageId={OutboxMessageId}, AggregateId={AggregateId}, MessageType={MessageType}, Destination={Destination}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    message.ItemId,
                    message.AggregateId,
                    message.MessageType,
                    message.Destination,
                    message.ProjectKey,
                    message.TenantId,
                    message.OrganizationId);

                return true;
            }
            catch (Exception ex)
            {
                var attemptCount = message.AttemptCount + 1;
                var maxAttempts = Math.Max(1, _configuration.GetValue<int?>("MailOutbox:MaxPublishAttempts") ?? 10);
                var status = attemptCount >= maxAttempts
                    ? OutboxMessageStatus.DeadLettered
                    : OutboxMessageStatus.FailedRetryable;
                var nextAttemptUtc = status == OutboxMessageStatus.DeadLettered
                    ? DateTime.UtcNow
                    : DateTime.UtcNow.AddSeconds(GetRetryDelaySeconds(attemptCount));

                await _mailRepository.MarkOutboxMessageFailedAsync(messageTenantId, message.ItemId, attemptCount, nextAttemptUtc, status, ex.Message);

                _logger.LogError(
                    ex,
                    "Failed to publish mail outbox message. OutboxMessageId={OutboxMessageId}, AggregateId={AggregateId}, MessageType={MessageType}, Destination={Destination}, AttemptCount={AttemptCount}, Status={Status}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    message.ItemId,
                    message.AggregateId,
                    message.MessageType,
                    message.Destination,
                    attemptCount,
                    status,
                    message.ProjectKey,
                    message.TenantId,
                    message.OrganizationId);

                if (status != OutboxMessageStatus.DeadLettered)
                {
                    await RequestProcessAsync(new MailOutboxMessage
                    {
                        ItemId = message.ItemId,
                        AggregateId = message.AggregateId,
                        MessageType = message.MessageType,
                        ProjectKey = message.ProjectKey,
                        TenantId = message.TenantId,
                        OrganizationId = message.OrganizationId,
                        NextAttemptUtc = nextAttemptUtc
                    });
                }

                return false;
            }
        }

        private Task PublishAsync(MailOutboxMessage message)
        {
            return message.MessageType switch
            {
                nameof(SendEmailCommand) => SendAsync(JsonSerializer.Deserialize<SendEmailCommand>(message.PayloadJson, SerializerOptions), message.Destination),
                nameof(NoAttachmentSendEmailCommand) => SendAsync(JsonSerializer.Deserialize<NoAttachmentSendEmailCommand>(message.PayloadJson, SerializerOptions), message.Destination),
                nameof(SmallAttachmentSendEmailCommand) => SendAsync(JsonSerializer.Deserialize<SmallAttachmentSendEmailCommand>(message.PayloadJson, SerializerOptions), message.Destination),
                nameof(LargeAttachmentSendEmailCommand) => SendAsync(JsonSerializer.Deserialize<LargeAttachmentSendEmailCommand>(message.PayloadJson, SerializerOptions), message.Destination),
                nameof(ProcessMailOutboxMessageCommand) => SendAsync(JsonSerializer.Deserialize<ProcessMailOutboxMessageCommand>(message.PayloadJson, SerializerOptions), message.Destination),
                nameof(MailSendCompletedEvent) => PublishToTopicAsync(JsonSerializer.Deserialize<MailSendCompletedEvent>(message.PayloadJson, SerializerOptions), message.Destination),
                nameof(CheckMailDeliveryStatusCommand) => SendAsync(JsonSerializer.Deserialize<CheckMailDeliveryStatusCommand>(message.PayloadJson, SerializerOptions), message.Destination),
                nameof(MailDeliveryStatusChangedEvent) => PublishToTopicAsync(JsonSerializer.Deserialize<MailDeliveryStatusChangedEvent>(message.PayloadJson, SerializerOptions), message.Destination),
                _ => throw new InvalidOperationException($"Unsupported mail outbox message type '{message.MessageType}'.")
            };
        }

        private Task SendAsync<T>(T? payload, string destination)
            where T : class
        {
            if (payload == null)
            {
                throw new InvalidOperationException("Mail outbox payload could not be deserialized.");
            }

            return _messageClient.SendToConsumerAsync(new ConsumerMessage<T>
            {
                ConsumerName = destination,
                Payload = payload
            });
        }

        private Task PublishToTopicAsync<T>(T? payload, string destination)
            where T : class
        {
            if (payload == null)
            {
                throw new InvalidOperationException("Mail outbox payload could not be deserialized.");
            }

            return _messageClient.SendToMassConsumerAsync(new ConsumerMessage<T>
            {
                ConsumerName = destination,
                Payload = payload
            });
        }

        private int GetRetryDelaySeconds(int attemptCount)
        {
            var initialDelay = Math.Max(1, _configuration.GetValue<int?>("MailOutbox:InitialRetryDelaySeconds") ?? 10);
            var maxDelay = Math.Max(initialDelay, _configuration.GetValue<int?>("MailOutbox:MaxRetryDelaySeconds") ?? 300);
            var exponentialDelay = initialDelay * Math.Pow(2, Math.Max(0, attemptCount - 1));

            return Math.Min(maxDelay, (int)exponentialDelay);
        }

        private static string? TryGetProperty<T>(T payload, string propertyName)
            where T : class
        {
            var property = typeof(T).GetProperty(propertyName);
            return property?.GetValue(payload) as string;
        }
    }
}
