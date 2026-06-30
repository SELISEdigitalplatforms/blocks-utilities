using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails
{
    public class MailDeliveryStatusService : IMailDeliveryStatusService
    {
        private readonly ILogger<MailDeliveryStatusService> _logger;
        private readonly IMailRepository _mailRepository;
        private readonly IExchangeMessageTraceClient _messageTraceClient;
        private readonly IMessageClient _messageClient;
        private readonly IConfiguration _configuration;

        public MailDeliveryStatusService(
            ILogger<MailDeliveryStatusService> logger,
            IMailRepository mailRepository,
            IExchangeMessageTraceClient messageTraceClient,
            IMessageClient messageClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _mailRepository = mailRepository;
            _messageTraceClient = messageTraceClient;
            _messageClient = messageClient;
            _configuration = configuration;
        }

        public async Task ProcessDeliveryStatusCheckAsync(CheckMailDeliveryStatusCommand command, CancellationToken cancellationToken = default)
        {
            var mailToBeSent = await _mailRepository.GetMailToBeSent(command.ItemId);
            if (mailToBeSent == null)
            {
                _logger.LogError("Delivery status check could not be processed because mail was not found. ItemId={ItemId}", command.ItemId);
                return;
            }

            try
            {
                var checkedAtUtc = DateTime.UtcNow;
                var results = await _messageTraceClient.GetDeliveryStatusesAsync(mailToBeSent, cancellationToken);
                var destination = CommunicationConstants.GetMailDeliveryStatusChangedQueueName(mailToBeSent.ProjectKey);

                foreach (var result in results)
                {
                    await _mailRepository.UpdateMailRecipientDeliveryStatusAsync(
                        mailToBeSent.ItemId,
                        result.Recipient,
                        result.Status,
                        result.StatusReason,
                        checkedAtUtc);

                    await _messageClient.SendToConsumerAsync(new ConsumerMessage<MailDeliveryStatusChangedEvent>
                    {
                        ConsumerName = destination,
                        Payload = new MailDeliveryStatusChangedEvent
                        {
                            ItemId = mailToBeSent.ItemId,
                            ProjectKey = mailToBeSent.ProjectKey,
                            TenantId = mailToBeSent.TenantId,
                            OrganizationId = mailToBeSent.OrganizationId,
                            Recipient = result.Recipient,
                            Status = result.Status,
                            StatusReason = result.StatusReason,
                            CheckedAtUtc = checkedAtUtc
                        }
                    });
                }

                _logger.LogInformation(
                    "Delivery status check completed. ItemId={ItemId}, ProjectKey={ProjectKey}, Attempt={Attempt}, ResultCount={ResultCount}",
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    command.Attempt,
                    results.Count);

                await RequeueIfNeededAsync(mailToBeSent.ItemId, mailToBeSent.ProjectKey, command, results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delivery status check failed. ItemId={ItemId}, ProjectKey={ProjectKey}", mailToBeSent.ItemId, mailToBeSent.ProjectKey);
                throw;
            }
        }

        private async Task RequeueIfNeededAsync(string itemId, string projectKey, CheckMailDeliveryStatusCommand command, IReadOnlyList<ExchangeMessageTraceResult> results)
        {
            if (!results.Any(result => IsNonTerminalStatus(result.Status)))
            {
                return;
            }

            var maxAttempts = Math.Max(1, _configuration.GetValue<int?>("MailDeliveryTracking:MaxAttempts") ?? 6);
            if (command.Attempt >= maxAttempts)
            {
                _logger.LogWarning(
                    "Delivery status check reached max attempts. ItemId={ItemId}, ProjectKey={ProjectKey}, Attempt={Attempt}, MaxAttempts={MaxAttempts}",
                    itemId,
                    projectKey,
                    command.Attempt,
                    maxAttempts);
                return;
            }

            var delayMinutes = GetRetryDelayMinutes(command.Attempt);
            var nextAttempt = command.Attempt + 1;

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<CheckMailDeliveryStatusCommand>
            {
                ConsumerName = CommunicationConstants.MailDeliveryStatusCheckQueueName,
                Payload = new CheckMailDeliveryStatusCommand
                {
                    ItemId = itemId,
                    Attempt = nextAttempt,
                    NotBeforeUtc = DateTime.UtcNow.AddMinutes(delayMinutes)
                }
            });

            _logger.LogInformation(
                "Requeued delivery status check. ItemId={ItemId}, ProjectKey={ProjectKey}, NextAttempt={NextAttempt}, DelayMinutes={DelayMinutes}",
                itemId,
                projectKey,
                nextAttempt,
                delayMinutes);
        }

        private int GetRetryDelayMinutes(int currentAttempt)
        {
            var configuredDelays = _configuration
                .GetSection("MailDeliveryTracking:RetryDelayMinutes")
                .Get<int[]>();

            if (configuredDelays is { Length: > 0 })
            {
                var index = Math.Clamp(currentAttempt - 1, 0, configuredDelays.Length - 1);
                return Math.Max(1, configuredDelays[index]);
            }

            return currentAttempt switch
            {
                1 => 15,
                2 => 30,
                _ => 60
            };
        }

        private static bool IsNonTerminalStatus(MailStatus status)
        {
            return status is MailStatus.Pending or MailStatus.Unknown;
        }
    }
}
