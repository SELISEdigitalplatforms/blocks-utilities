using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Mail.DomainService.Mails
{
    public class SendMailService : ISendMailService
    {
        private readonly ILogger<SendMailService> _logger;
        private readonly IMailRepository _mailRepository;
        private readonly SmtpClientProvider _smtpClientProvider;
        private readonly IMailSendConcurrencyLimiter _mailSendConcurrencyLimiter;
        private readonly IMessageClient _messageClient;
        private readonly IConfiguration _configuration;

        public SendMailService(
            ILogger<SendMailService> logger,
            IMailRepository mailRepository,
            SmtpClientProvider smtpClientProvider,
            IMailSendConcurrencyLimiter mailSendConcurrencyLimiter,
            IMessageClient messageClient,
            IConfiguration configuration
        )
        {
            _logger = logger;
            _mailRepository = mailRepository;
            _smtpClientProvider = smtpClientProvider;
            _mailSendConcurrencyLimiter = mailSendConcurrencyLimiter;
            _messageClient = messageClient;
            _configuration = configuration;
        }

        public async Task ProcessSendMailAsync(SendEmailCommand sendEmailCommand)
        {
            _logger.LogInformation("Processing send mail command. ItemId={ItemId}, MailCategory={MailCategory}", sendEmailCommand.ItemId, sendEmailCommand.MailCategory);

            await using var concurrencyLease = await _mailSendConcurrencyLimiter.AcquireAsync(sendEmailCommand.MailCategory);

            var mailToBeSent = await _mailRepository.GetMailToBeSent(sendEmailCommand.ItemId);
            if (mailToBeSent == null)
            {
                _logger.LogError("Mail send command could not be processed because mail was not found. ItemId={ItemId}", sendEmailCommand.ItemId);
                return;
            }

            var success = false;
            string? failureReason = null;

            try
            {
                var smtpClient = _smtpClientProvider.GetSmtpClient(mailToBeSent);
                var mailBody = BuildMailBody(mailToBeSent);

                success = await smtpClient.SendAsync(mailToBeSent, mailBody);
                failureReason = success ? null : "ProviderReturnedFalse";

                if (success)
                {
                    await TrackSubmissionAndQueueDeliveryCheckAsync(mailToBeSent);
                }
            }
            catch (Exception ex)
            {
                failureReason = ex.GetType().Name;
                _logger.LogError(ex, "Mail provider submission failed. ItemId={ItemId}, MailCategory={MailCategory}", mailToBeSent.ItemId, mailToBeSent.MailCategory);
            }
            finally
            {
                await PublishMailSendCompletedEventAsync(mailToBeSent, success, failureReason);
            }

            LogSendResult(mailToBeSent, success);
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
            var destination = CommunicationConstants.GetMailSendCompletedQueueName(projectKey);
            var payload = new MailSendCompletedEvent
            {
                ItemId = mailToBeSent.ItemId,
                ProjectKey = projectKey,
                TenantId = mailToBeSent.TenantId ?? string.Empty,
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
                await _messageClient.SendToConsumerAsync(new ConsumerMessage<MailSendCompletedEvent>
                {
                    ConsumerName = destination,
                    Payload = payload
                });

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

        private async Task TrackSubmissionAndQueueDeliveryCheckAsync(MailToBeSent mailToBeSent)
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

                await _mailRepository.UpdateMailSubmissionTrackingAsync(
                    mailToBeSent.ItemId,
                    mailToBeSent.InternetMessageId ?? string.Empty,
                    submittedAtUtc,
                    senderAddress,
                    recipientStatuses);

                var delayMinutes = Math.Max(0, _configuration.GetValue<int?>("MailDeliveryTracking:InitialDelayInMinutes") ?? 5);
                await _messageClient.SendToConsumerAsync(new ConsumerMessage<CheckMailDeliveryStatusCommand>
                {
                    ConsumerName = CommunicationConstants.MailDeliveryStatusCheckQueueName,
                    Payload = new CheckMailDeliveryStatusCommand
                    {
                        ItemId = mailToBeSent.ItemId,
                        NotBeforeUtc = submittedAtUtc.AddMinutes(delayMinutes),
                        Attempt = 1
                    }
                });
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
