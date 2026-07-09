using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Mail.DomainService.Mails.Services.Core
{
    public class SendMailService : ISendMailService
    {
        private readonly ILogger<SendMailService> _logger;
        private readonly IMailRepository _mailRepository;
        private readonly SmtpClientProvider _smtpClientProvider;
        private readonly IMailSendConcurrencyLimiter _mailSendConcurrencyLimiter;
        private readonly IMailProviderRateLimiter _mailProviderRateLimiter;
        private readonly IMailOutboxService _mailOutboxService;
        private readonly IConfiguration _configuration;

        public SendMailService(
            ILogger<SendMailService> logger,
            IMailRepository mailRepository,
            SmtpClientProvider smtpClientProvider,
            IMailSendConcurrencyLimiter mailSendConcurrencyLimiter,
            IMailProviderRateLimiter mailProviderRateLimiter,
            IMailOutboxService mailOutboxService,
            IConfiguration configuration
        )
        {
            _logger = logger;
            _mailRepository = mailRepository;
            _smtpClientProvider = smtpClientProvider;
            _mailSendConcurrencyLimiter = mailSendConcurrencyLimiter;
            _mailProviderRateLimiter = mailProviderRateLimiter;
            _mailOutboxService = mailOutboxService;
            _configuration = configuration;
        }

        public async Task ProcessSendMailAsync(SendEmailCommand sendEmailCommand)
        {
            _logger.LogInformation("Processing send mail command. ItemId={ItemId}, MailCategory={MailCategory}", sendEmailCommand.ItemId, sendEmailCommand.MailCategory);

            await using var concurrencyLease = await _mailSendConcurrencyLimiter.AcquireAsync(sendEmailCommand.MailCategory);

            var commandTenantId = sendEmailCommand.TenantId ?? string.Empty;
            var mailToBeSent = await _mailRepository.GetMailToBeSent(commandTenantId, sendEmailCommand.ItemId);
            if (mailToBeSent == null)
            {
                _logger.LogError(
                    "Mail send command could not be processed because mail was not found. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    sendEmailCommand.ItemId,
                    sendEmailCommand.ProjectKey,
                    commandTenantId,
                    sendEmailCommand.OrganizationId);
                return;
            }

            if (mailToBeSent.SubmissionStatus == MailSubmissionStatus.Accepted)
            {
                _logger.LogInformation(
                    "Skipping mail send command because provider submission was already accepted. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId);
                return;
            }

            var providerRateLimitResult = await _mailProviderRateLimiter.CheckAsync(mailToBeSent);
            if (!providerRateLimitResult.IsAllowed)
            {
                await QueueProviderRateLimitedRetryAsync(mailToBeSent, sendEmailCommand, providerRateLimitResult);
                return;
            }

            var processingLockTimeoutMinutes = Math.Max(1, _configuration.GetValue<int?>("MicrosoftGraphMail:SubmissionProcessingLockTimeoutMinutes") ?? 30);
            var tenantId = mailToBeSent.TenantId ?? commandTenantId;
            var claimed = await _mailRepository.TryStartMailSubmissionAsync(tenantId, mailToBeSent.ItemId, DateTime.UtcNow, processingLockTimeoutMinutes);
            if (!claimed)
            {
                _logger.LogInformation(
                    "Skipping mail send command because it could not be claimed for submission. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}, SubmissionStatus={SubmissionStatus}",
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId,
                    mailToBeSent.SubmissionStatus);
                return;
            }

            mailToBeSent = await _mailRepository.GetMailToBeSent(tenantId, sendEmailCommand.ItemId);
            var submissionResult = MailSubmissionResult.Failed("UnknownSubmissionFailure", true);

            try
            {
                var smtpClient = _smtpClientProvider.GetSmtpClient(mailToBeSent);
                var mailBody = BuildMailBody(mailToBeSent);

                submissionResult = await smtpClient.SendAsync(mailToBeSent, mailBody);

                if (submissionResult.IsAccepted)
                {
                    await TrackSubmissionAndQueueDeliveryCheckAsync(mailToBeSent, submissionResult);
                    await PublishMailSendCompletedEventAsync(mailToBeSent, true, null);
                }
                else
                {
                    await HandleSubmissionFailureAsync(mailToBeSent, sendEmailCommand, submissionResult);
                }
            }
            catch (Exception ex)
            {
                submissionResult = MailSubmissionResult.Failed(ex.GetType().Name, true);
                _logger.LogError(ex, "Mail provider submission failed. ItemId={ItemId}, MailCategory={MailCategory}", mailToBeSent.ItemId, mailToBeSent.MailCategory);
                await HandleSubmissionFailureAsync(mailToBeSent, sendEmailCommand, submissionResult);
            }

            LogSendResult(mailToBeSent, submissionResult.IsAccepted);
        }

        private void LogSendResult(MailToBeSent mailToBeSent, bool success)
        {
            var recipients = "HIDDEN recipients (" + string.Join(", ", (mailToBeSent.To ?? Enumerable.Empty<string>()).Select(x => "*****@" + x.Split("@").LastOrDefault())) + ")";
            var subject = mailToBeSent.EmailTemplate?.TemplateSubject ?? string.Empty;
            var templateName = mailToBeSent.EmailTemplate?.Name ?? string.Empty;

            if (success)
            {
                var logMessage = string.Format("SUCCESS:\nTo: {0}\nSubject: {1}\nTime: {2}\nTemplate Name: {3}", recipients, subject, DateTime.Now, templateName);
                _logger.LogInformation(
                    "{LogMessage}. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    logMessage,
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId);
            }
            else
            {
                var logMessage = string.Format("FAILED:\nTo: {0}\nSubject: {1}\nTime: {2}\nTemplate Name: {3}", recipients, subject, DateTime.Now, templateName);
                _logger.LogError(
                    "{LogMessage}. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    logMessage,
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId);
            }
        }

        private async Task PublishMailSendCompletedEventAsync(MailToBeSent mailToBeSent, bool success, string? failureReason)
        {
            var projectKey = mailToBeSent.ProjectKey;
            var tenantId = mailToBeSent.TenantId ?? string.Empty;
            var destination = CommunicationConstants.MailSendCompletedTopicName;
            var payload = new MailSendCompletedEvent
            {
                ItemId = mailToBeSent.ItemId,
                ProjectKey = projectKey,
                TenantId = tenantId,
                OrganizationId = mailToBeSent.OrganizationId ?? string.Empty,
                Purpose = mailToBeSent.Name ?? string.Empty,
                MailCategory = mailToBeSent.MailCategory,
                IsSuccess = success,
                CompletedAtUtc = DateTime.UtcNow,
                RecipientCount = CountRecipients(mailToBeSent),
                AttachmentCount = mailToBeSent.Attachments?.Count() ?? 0,
                IsTestMail = mailToBeSent.IsTestMail,
                FailureReason = success ? null : failureReason
            };

            try
            {
                await _mailOutboxService.EnqueueAsync(
                    mailToBeSent.ItemId,
                    destination,
                    payload,
                    $"mail-send-completed:{mailToBeSent.ItemId}:{success}");

                _logger.LogInformation(
                    "Published mail send completed event. ItemId={ItemId}, ProjectKey={ProjectKey}, Destination={Destination}, IsSuccess={IsSuccess}",
                    mailToBeSent.ItemId,
                    projectKey,
                    destination,
                    success);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to publish mail send completed event. ItemId={ItemId}, ProjectKey={ProjectKey}, IsSuccess={IsSuccess}",
                    mailToBeSent.ItemId,
                    projectKey,
                    success);
            }
        }        

        private async Task TrackSubmissionAndQueueDeliveryCheckAsync(MailToBeSent mailToBeSent, MailSubmissionResult submissionResult)
        {
            try
            {
                var submittedAtUtc = DateTime.UtcNow;
                var senderAddress = mailToBeSent.MailServerConfiguration?.SenderAddress ?? string.Empty;
                var recipientStatuses = GetRecipients(mailToBeSent)
                    .Select(recipient => new MailRecipientDeliveryStatus
                    {
                        Recipient = recipient,
                        Status = MailStatus.Pending
                    })
                    .ToList();

                mailToBeSent.SubmittedAtUtc = submittedAtUtc;
                mailToBeSent.SenderAddress = senderAddress;
                mailToBeSent.RecipientDeliveryStatuses = recipientStatuses;

                await _mailRepository.UpdateMailSubmissionAcceptedAsync(
                    mailToBeSent.TenantId ?? string.Empty,
                    mailToBeSent.ItemId,
                    mailToBeSent.InternetMessageId ?? string.Empty,
                    submittedAtUtc,
                    senderAddress,
                    recipientStatuses,
                    submissionResult);

                if (mailToBeSent.MailServerConfiguration?.SmtpClient == SmtpClient.MsGraph)
                {
                    var delayMinutes = Math.Max(0, _configuration.GetValue<int?>("MailDeliveryTracking:InitialDelayInMinutes") ?? 5);
                    await _mailOutboxService.EnqueueAsync(
                        mailToBeSent.ItemId,
                        CommunicationConstants.MailDeliveryStatusCheckQueueName,
                        new CheckMailDeliveryStatusCommand
                        {
                            ItemId = mailToBeSent.ItemId,
                            ProjectKey = mailToBeSent.ProjectKey,
                            TenantId = mailToBeSent.TenantId,
                            OrganizationId = mailToBeSent.OrganizationId,
                            NotBeforeUtc = submittedAtUtc.AddMinutes(delayMinutes),
                            Attempt = 1
                        },
                        $"mail-delivery-check:{mailToBeSent.ItemId}:attempt:1",
                        submittedAtUtc.AddMinutes(delayMinutes));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to track mail submission or queue delivery status check. ItemId={ItemId}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    mailToBeSent.ItemId,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId);
            }
        }

        private async Task HandleSubmissionFailureAsync(MailToBeSent mailToBeSent, SendEmailCommand sendEmailCommand, MailSubmissionResult submissionResult)
        {
            var maxAttempts = Math.Max(1, _configuration.GetValue<int?>("MicrosoftGraphMail:MaxSubmissionRetryAttempts") ?? 5);
            var shouldRetry = submissionResult.IsRetryable && mailToBeSent.SubmissionAttemptCount < maxAttempts;

            if (shouldRetry)
            {
                await _mailRepository.UpdateMailSubmissionFailedAsync(mailToBeSent.TenantId ?? string.Empty, mailToBeSent.ItemId, MailSubmissionStatus.FailedRetryable, submissionResult);
                await QueueSubmissionRetryAsync(mailToBeSent, sendEmailCommand, submissionResult);

                _logger.LogWarning(
                    "Mail provider submission failed and will be retried. ItemId={ItemId}, Attempt={Attempt}, MaxAttempts={MaxAttempts}, FailureReason={FailureReason}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                    mailToBeSent.ItemId,
                    mailToBeSent.SubmissionAttemptCount,
                    maxAttempts,
                    submissionResult.FailureReason,
                    mailToBeSent.ProjectKey,
                    mailToBeSent.TenantId,
                    mailToBeSent.OrganizationId);
                return;
            }

            await _mailRepository.UpdateMailSubmissionFailedAsync(mailToBeSent.TenantId ?? string.Empty, mailToBeSent.ItemId, MailSubmissionStatus.FailedPermanent, submissionResult);
            await PublishMailSendCompletedEventAsync(mailToBeSent, false, submissionResult.FailureReason);

            _logger.LogError(
                "Mail provider submission failed permanently. ItemId={ItemId}, Attempt={Attempt}, MaxAttempts={MaxAttempts}, FailureReason={FailureReason}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                mailToBeSent.ItemId,
                mailToBeSent.SubmissionAttemptCount,
                maxAttempts,
                submissionResult.FailureReason,
                mailToBeSent.ProjectKey,
                mailToBeSent.TenantId,
                mailToBeSent.OrganizationId);
        }

        private async Task QueueSubmissionRetryAsync(MailToBeSent mailToBeSent, SendEmailCommand sendEmailCommand, MailSubmissionResult submissionResult)
        {
            var nextAttempt = Math.Max(sendEmailCommand.Attempt + 1, mailToBeSent.SubmissionAttemptCount + 1);
            var delaySeconds = submissionResult.RetryAfterSeconds.GetValueOrDefault(GetSubmissionRetryDelaySeconds(nextAttempt - 1));
            var nextAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
            var deduplicationKey = $"mail-send:{mailToBeSent.ItemId}:attempt:{nextAttempt}";

            switch (mailToBeSent.MailCategory)
            {
                case MailCategory.SmallAttachment:
                    await _mailOutboxService.EnqueueAsync(
                        mailToBeSent.ItemId,
                        CommunicationConstants.SmallAttachmentMailQueueName,
                        CreateSendCommand<SmallAttachmentSendEmailCommand>(mailToBeSent, nextAttempt),
                        deduplicationKey,
                        nextAttemptUtc);
                    break;

                case MailCategory.LargeAttachment:
                    await _mailOutboxService.EnqueueAsync(
                        mailToBeSent.ItemId,
                        CommunicationConstants.LargeAttachmentMailQueueName,
                        CreateSendCommand<LargeAttachmentSendEmailCommand>(mailToBeSent, nextAttempt),
                        deduplicationKey,
                        nextAttemptUtc);
                    break;

                default:
                    await _mailOutboxService.EnqueueAsync(
                        mailToBeSent.ItemId,
                        CommunicationConstants.NoAttachmentMailQueueName,
                        CreateSendCommand<NoAttachmentSendEmailCommand>(mailToBeSent, nextAttempt),
                        deduplicationKey,
                        nextAttemptUtc);
                    break;
            }
        }

        private async Task QueueProviderRateLimitedRetryAsync(
            MailToBeSent mailToBeSent,
            SendEmailCommand sendEmailCommand,
            MailRateLimitResult rateLimitResult)
        {
            var retryAfterSeconds = Math.Max(1, rateLimitResult.RetryAfterSeconds);
            var nextAttemptUtc = DateTime.UtcNow.AddSeconds(retryAfterSeconds);
            var deduplicationKey = $"mail-send-provider-rate-limit:{mailToBeSent.ItemId}:attempt:{sendEmailCommand.Attempt}:not-before:{nextAttemptUtc.Ticks}";

            switch (mailToBeSent.MailCategory)
            {
                case MailCategory.SmallAttachment:
                    await _mailOutboxService.EnqueueAsync(
                        mailToBeSent.ItemId,
                        CommunicationConstants.SmallAttachmentMailQueueName,
                        CreateSendCommand<SmallAttachmentSendEmailCommand>(mailToBeSent, sendEmailCommand.Attempt),
                        deduplicationKey,
                        nextAttemptUtc);
                    break;

                case MailCategory.LargeAttachment:
                    await _mailOutboxService.EnqueueAsync(
                        mailToBeSent.ItemId,
                        CommunicationConstants.LargeAttachmentMailQueueName,
                        CreateSendCommand<LargeAttachmentSendEmailCommand>(mailToBeSent, sendEmailCommand.Attempt),
                        deduplicationKey,
                        nextAttemptUtc);
                    break;

                default:
                    await _mailOutboxService.EnqueueAsync(
                        mailToBeSent.ItemId,
                        CommunicationConstants.NoAttachmentMailQueueName,
                        CreateSendCommand<NoAttachmentSendEmailCommand>(mailToBeSent, sendEmailCommand.Attempt),
                        deduplicationKey,
                        nextAttemptUtc);
                    break;
            }

            _logger.LogWarning(
                "Mail provider rate limit reached; send command requeued without Graph submission. ItemId={ItemId}, Scope={Scope}, RetryAfterSeconds={RetryAfterSeconds}, ProjectKey={ProjectKey}, TenantId={TenantId}, OrganizationId={OrganizationId}",
                mailToBeSent.ItemId,
                rateLimitResult.Scope,
                retryAfterSeconds,
                mailToBeSent.ProjectKey,
                mailToBeSent.TenantId,
                mailToBeSent.OrganizationId);
        }

        private static TCommand CreateSendCommand<TCommand>(MailToBeSent mailToBeSent, int attempt)
            where TCommand : SendEmailCommand, new()
        {
            return new TCommand
            {
                ItemId = mailToBeSent.ItemId,
                Attempt = attempt,
                ProjectKey = mailToBeSent.ProjectKey,
                TenantId = mailToBeSent.TenantId,
                OrganizationId = mailToBeSent.OrganizationId
            };
        }

        private int GetSubmissionRetryDelaySeconds(int failedAttempt)
        {
            var initialDelay = Math.Max(1, _configuration.GetValue<int?>("MicrosoftGraphMail:InitialSubmissionRetryDelaySeconds") ?? 30);
            var maxDelay = Math.Max(initialDelay, _configuration.GetValue<int?>("MicrosoftGraphMail:MaxSubmissionRetryDelaySeconds") ?? 900);
            var exponentialDelay = initialDelay * Math.Pow(2, Math.Max(0, failedAttempt - 1));

            return Math.Min(maxDelay, (int)exponentialDelay);
        }

        private static int CountRecipients(MailToBeSent mailToBeSent)
        {
            return (mailToBeSent.To?.Count() ?? 0)
                + (mailToBeSent.Cc?.Count() ?? 0)
                + (mailToBeSent.Bcc?.Count() ?? 0);
        }

        private static IEnumerable<string> GetRecipients(MailToBeSent mailToBeSent)
        {
            return (mailToBeSent.To ?? Enumerable.Empty<string>())
                .Concat(mailToBeSent.Cc ?? Enumerable.Empty<string>())
                .Concat(mailToBeSent.Bcc ?? Enumerable.Empty<string>())
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public MailBody BuildMailBody(MailToBeSent mailToBeSent)
        {
            return new MailBody
            {
                Subject = BuildSubject(mailToBeSent.EmailTemplate.TemplateSubject, mailToBeSent.SubjectDataContext),
                Body = BuildBody(mailToBeSent.EmailTemplate.TemplateBody, mailToBeSent.BodyDataContext)
            };
        }

        public static string BuildBody(string templateBody, Dictionary<string, string> placeHolderValues)
        {
            var body = templateBody;

            foreach (var placeHolderValue in placeHolderValues)
            {
                body = body.Replace("{{" + placeHolderValue.Key + "}}", WebUtility.HtmlEncode(placeHolderValue.Value));
            }

            return body;
        }

        public static string BuildSubject(string templateSubject, Dictionary<string, string> placeHolderValues)
        {
            var body = templateSubject;

            foreach (var placeHolderValue in placeHolderValues)
            {
                body = body.Replace("{{" + placeHolderValue.Key + "}}", placeHolderValue.Value);
            }

            return body;
        }
    }
}
