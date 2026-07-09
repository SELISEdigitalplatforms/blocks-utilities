using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Services.DeliveryTracking
{
    public class MailDeliveryStatusService : IMailDeliveryStatusService
    {
        private readonly ILogger<MailDeliveryStatusService> _logger;
        private readonly IMailRepository _mailRepository;
        private readonly IExchangeMessageTraceClient _messageTraceClient;
        private readonly IMailOutboxService _mailOutboxService;
        private readonly IConfiguration _configuration;

        public MailDeliveryStatusService(
            ILogger<MailDeliveryStatusService> logger,
            IMailRepository mailRepository,
            IExchangeMessageTraceClient messageTraceClient,
            IMailOutboxService mailOutboxService,
            IConfiguration configuration)
        {
            _logger = logger;
            _mailRepository = mailRepository;
            _messageTraceClient = messageTraceClient;
            _mailOutboxService = mailOutboxService;
            _configuration = configuration;
        }

        public async Task ProcessDeliveryStatusCheckAsync(CheckMailDeliveryStatusCommand command, CancellationToken cancellationToken = default)
        {
            var commandTenantId = command.TenantId ?? string.Empty;
            var mailToBeSent = await _mailRepository.GetMailToBeSent(commandTenantId, command.ItemId);
            if (mailToBeSent == null)
            {
                _logger.LogError(
                    "Delivery status check could not be processed because mail was not found. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    command.ItemId,
                    command.ProjectKey,
                    commandTenantId,
                    command.OrganizationId);
                return;
            }

            try
            {
                var checkedAtUtc = DateTime.UtcNow;
                var results = await _messageTraceClient.GetDeliveryStatusesAsync(mailToBeSent, cancellationToken);
                var destination = CommunicationConstants.MailDeliveryStatusChangedTopicName;

                foreach (var result in results)
                {
                    await _mailRepository.UpdateMailRecipientDeliveryStatusAsync(
                        mailToBeSent.TenantId ?? string.Empty,
                        mailToBeSent.ItemId,
                        result.Recipient,
                        result.Status,
                        result.StatusReason,
                        checkedAtUtc);

                    await _mailOutboxService.EnqueueAsync(
                        mailToBeSent.ItemId,
                        destination,
                        new MailDeliveryStatusChangedEvent
                        {
                            ItemId = mailToBeSent.ItemId,
                            ProjectKey = mailToBeSent.ProjectKey,
                            TenantId = mailToBeSent.TenantId,
                            OrganizationId = mailToBeSent.OrganizationId,
                            Recipient = result.Recipient,
                            Status = result.Status,
                            StatusReason = result.StatusReason,
                            CheckedAtUtc = checkedAtUtc
                        },
                        $"mail-delivery-status-changed:{mailToBeSent.ItemId}:{result.Recipient}:{checkedAtUtc:O}");
                }

                _logger.LogInformation(
                    "Delivery status check completed. ItemId={ItemId}, ProjectKey={ProjectKey}, Attempt={Attempt}, ResultCount={ResultCount}",
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    command.Attempt,
                    results.Count);

                await RequeueIfNeededAsync(mailToBeSent, command, results);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Delivery status check failed. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId);
                throw;
            }
        }

        private async Task RequeueIfNeededAsync(MailToBeSent mailToBeSent, CheckMailDeliveryStatusCommand command, IReadOnlyList<ExchangeMessageTraceResult> results)
        {
            if (!results.Any(result => IsNonTerminalStatus(result.Status)))
            {
                return;
            }

            var maxAttempts = Math.Max(1, _configuration.GetValue<int?>("MailDeliveryTracking:MaxAttempts") ?? 6);
            if (command.Attempt >= maxAttempts)
            {
                _logger.LogWarning(
                    "Delivery status check reached max attempts. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}, Attempt={Attempt}, MaxAttempts={MaxAttempts}",
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId,
                    command.Attempt,
                    maxAttempts);
                return;
            }

            var delayMinutes = GetRetryDelayMinutes(command.Attempt);
            var nextAttempt = command.Attempt + 1;

            var nextCheckAtUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
            await _mailOutboxService.EnqueueAsync(
                mailToBeSent.ItemId,
                CommunicationConstants.MailDeliveryStatusCheckQueueName,
                new CheckMailDeliveryStatusCommand
                {
                    ItemId = mailToBeSent.ItemId,
                    ProjectKey = mailToBeSent.ProjectKey,
                    TenantId = mailToBeSent.TenantId,
                    OrganizationId = mailToBeSent.OrganizationId,
                    Attempt = nextAttempt,
                    NotBeforeUtc = nextCheckAtUtc
                },
                $"mail-delivery-check:{mailToBeSent.ItemId}:attempt:{nextAttempt}",
                nextCheckAtUtc);

            _logger.LogInformation(
                "Requeued delivery status check. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}, NextAttempt={NextAttempt}, DelayMinutes={DelayMinutes}",
                mailToBeSent.ItemId,
                mailToBeSent.ProjectKey,
                mailToBeSent.TenantId,
                mailToBeSent.OrganizationId,
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
