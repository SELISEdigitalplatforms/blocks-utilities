using Mail.DomainService.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.Messages.Item.Attachments.CreateUploadSession;
using Microsoft.Kiota.Abstractions;

namespace Mail.DomainService.Mails.Services.Transport
{
    public class MicrosoftGraphServiceClient : ISmtpClient
    {
        public const long SmallAttachmentMaxSizeInBytes = 3L * 1024 * 1024;
        public const long GraphAttachmentMaxSizeInBytes = 150L * 1024 * 1024;

        private const int LargeAttachmentSliceSize = 320 * 1024;

        private readonly IMicrosoftGraphClientFactory _graphClientFactory;
        private readonly IMailAttachmentProvider _attachmentProvider;
        private readonly ILogger<MicrosoftGraphServiceClient> _logger;

        public MicrosoftGraphServiceClient(
            IMicrosoftGraphClientFactory graphClientFactory,
            IMailAttachmentProvider attachmentProvider,
            ILogger<MicrosoftGraphServiceClient> logger)
        {
            _graphClientFactory = graphClientFactory;
            _attachmentProvider = attachmentProvider;
            _logger = logger;
        }

        public async Task<MailSubmissionResult> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            ArgumentNullException.ThrowIfNull(mailToBeSent);
            ArgumentNullException.ThrowIfNull(mailBody);

            var configuration = mailToBeSent.MailServerConfiguration;
            if (!ValidateConfiguration(configuration))
            {
                return MailSubmissionResult.Failed("InvalidMicrosoftGraphConfiguration", false);
            }

            var graphClient = _graphClientFactory.Create(configuration);
            var message = BuildMessage(mailToBeSent, mailBody);
            IReadOnlyList<MailAttachment> attachments = Array.Empty<MailAttachment>();

            try
            {
                _logger.LogInformation(
                    "Creating Microsoft Graph draft message. SenderAddress={SenderAddress}, RecipientCount={RecipientCount}",
                    configuration.SenderAddress,
                    GetRecipientCount(mailToBeSent));

                var draftMessage = await graphClient.Users[configuration.SenderAddress].Messages.PostAsync(message);

                if (string.IsNullOrWhiteSpace(draftMessage?.Id))
                {
                    _logger.LogError("Microsoft Graph draft creation did not return a message id. SenderAddress={SenderAddress}", configuration.SenderAddress);
                    return MailSubmissionResult.Failed("GraphDraftMessageIdMissing", true);
                }

                mailToBeSent.InternetMessageId = draftMessage.InternetMessageId ?? string.Empty;

                attachments = await _attachmentProvider.GetAttachmentsAsync(mailToBeSent);

                _logger.LogInformation(
                    "Microsoft Graph draft created. SenderAddress={SenderAddress}, MessageId={MessageId}, AttachmentCount={AttachmentCount}",
                    configuration.SenderAddress,
                    draftMessage.Id,
                    attachments.Count);

                foreach (var attachment in attachments)
                {
                    await AddAttachmentAsync(graphClient, configuration.SenderAddress, draftMessage.Id, attachment);
                }

                _logger.LogInformation(
                    "Sending Microsoft Graph draft message. SenderAddress={SenderAddress}, MessageId={MessageId}",
                    configuration.SenderAddress,
                    draftMessage.Id);

                await graphClient.Users[configuration.SenderAddress].Messages[draftMessage.Id].Send.PostAsync();

                _logger.LogInformation(
                    "Microsoft Graph message sent successfully. SenderAddress={SenderAddress}, MessageId={MessageId}",
                    configuration.SenderAddress,
                    draftMessage.Id);

                return MailSubmissionResult.Accepted(202);
            }
            catch (ODataError ex)
            {
                _logger.LogError(ex, "Microsoft Graph OData error while sending mail. SenderAddress={SenderAddress}, ErrorCode={ErrorCode}", configuration.SenderAddress, ex.Error?.Code);
                return MailSubmissionResult.Failed(ex.Error?.Code ?? nameof(ODataError), false);
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Microsoft Graph API error while sending mail. SenderAddress={SenderAddress}, StatusCode={StatusCode}", configuration.SenderAddress, ex.ResponseStatusCode);
                return MailSubmissionResult.Failed(
                    $"GraphApiException:{ex.ResponseStatusCode}",
                    IsRetryableStatusCode(ex.ResponseStatusCode),
                    ex.ResponseStatusCode,
                    retryAfterSeconds: GetRetryAfterSeconds(ex));
            }
            catch (MailAttachmentException ex)
            {
                _logger.LogError(ex, "Attachment resolution failed while sending Microsoft Graph mail. SenderAddress={SenderAddress}", configuration.SenderAddress);
                return MailSubmissionResult.Failed(ex.GetType().Name, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while sending Microsoft Graph mail. SenderAddress={SenderAddress}", configuration.SenderAddress);
                return MailSubmissionResult.Failed(ex.GetType().Name, true);
            }
            finally
            {
                foreach (var attachment in attachments)
                {
                    await attachment.DisposeAsync();
                }
            }
        }

        public static Message BuildMessage(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            var message = new Message
            {
                Subject = mailBody.Subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = mailBody.Body
                },
                ToRecipients = GetRecipientAddresses(mailToBeSent.To),
                CcRecipients = GetRecipientAddresses(mailToBeSent.Cc),
                BccRecipients = GetRecipientAddresses(mailToBeSent.Bcc),
                ReplyTo = GetRecipientAddresses(mailToBeSent.ReplyTo)
            };

            if (!string.IsNullOrWhiteSpace(mailToBeSent.ItemId))
            {
                message.InternetMessageHeaders =
                [
                    new InternetMessageHeader
                    {
                        Name = "x-blocks-mail-item-id",
                        Value = mailToBeSent.ItemId
                    }
                ];
            }

            return message;
        }

        public static List<Recipient> GetRecipientAddresses(IEnumerable<string>? emailAddresses)
        {
            return emailAddresses?
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .Select(email => new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = email
                    }
                })
                .ToList() ?? [];
        }

        private async Task AddAttachmentAsync(GraphServiceClient graphClient, string senderAddress, string messageId, MailAttachment attachment)
        {
            if (attachment.SizeInBytes > GraphAttachmentMaxSizeInBytes)
            {
                throw new MailAttachmentException($"Attachment '{attachment.FileId}' exceeds Microsoft Graph's supported attachment size limit.");
            }

            if (attachment.Content.CanSeek)
            {
                attachment.Content.Position = 0;
            }

            if (attachment.SizeInBytes <= SmallAttachmentMaxSizeInBytes)
            {
                _logger.LogInformation(
                    "Adding small Microsoft Graph attachment. MessageId={MessageId}, FileName={FileName}, SizeInBytes={SizeInBytes}",
                    messageId,
                    attachment.FileName,
                    attachment.SizeInBytes);

                using var memoryStream = new MemoryStream((int)attachment.SizeInBytes);
                await attachment.Content.CopyToAsync(memoryStream);

                var fileAttachment = new FileAttachment
                {
                    OdataType = "#microsoft.graph.fileAttachment",
                    Name = attachment.FileName,
                    ContentType = attachment.ContentType,
                    ContentBytes = memoryStream.ToArray()
                };

                await graphClient.Users[senderAddress].Messages[messageId].Attachments.PostAsync(fileAttachment);
                return;
            }

            _logger.LogInformation(
                "Adding large Microsoft Graph attachment with upload session. MessageId={MessageId}, FileName={FileName}, SizeInBytes={SizeInBytes}",
                messageId,
                attachment.FileName,
                attachment.SizeInBytes);

            var uploadSessionRequest = new CreateUploadSessionPostRequestBody
            {
                AttachmentItem = new AttachmentItem
                {
                    AttachmentType = AttachmentType.File,
                    Name = attachment.FileName,
                    Size = attachment.SizeInBytes
                }
            };

            var uploadSession = await graphClient.Users[senderAddress]
                .Messages[messageId]
                .Attachments
                .CreateUploadSession
                .PostAsync(uploadSessionRequest);

            if (uploadSession == null)
            {
                throw new MailAttachmentException($"Microsoft Graph did not create an upload session for attachment '{attachment.FileId}'.");
            }

            var uploadTask = new LargeFileUploadTask<FileAttachment>(
                uploadSession,
                attachment.Content,
                LargeAttachmentSliceSize,
                graphClient.RequestAdapter);

            var uploadResult = await uploadTask.UploadAsync();

            if (!uploadResult.UploadSucceeded)
            {
                throw new MailAttachmentException($"Microsoft Graph upload session failed for attachment '{attachment.FileId}'.");
            }

            _logger.LogInformation(
                "Large Microsoft Graph attachment uploaded successfully. MessageId={MessageId}, FileName={FileName}, SizeInBytes={SizeInBytes}",
                messageId,
                attachment.FileName,
                attachment.SizeInBytes);
        }

        private bool ValidateConfiguration(MailServerConfiguration? configuration)
        {
            if (configuration == null)
            {
                _logger.LogError("Microsoft Graph mail configuration is missing.");
                return false;
            }

            var missingFields = new List<string>();

            AddIfMissing(missingFields, configuration.TenantId, nameof(configuration.TenantId));
            AddIfMissing(missingFields, configuration.SenderUserName, nameof(configuration.SenderUserName));
            AddIfMissing(missingFields, configuration.AccountPassword, nameof(configuration.AccountPassword));
            AddIfMissing(missingFields, configuration.SenderAddress, nameof(configuration.SenderAddress));

            if (missingFields.Count == 0)
            {
                return true;
            }

            _logger.LogError("Microsoft Graph mail configuration is invalid. MissingFields={MissingFields}", string.Join(", ", missingFields));
            return false;
        }

        private static void AddIfMissing(ICollection<string> missingFields, string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missingFields.Add(fieldName);
            }
        }

        private static int GetRecipientCount(MailToBeSent mailToBeSent)
        {
            return (mailToBeSent.To?.Count() ?? 0)
                + (mailToBeSent.Cc?.Count() ?? 0)
                + (mailToBeSent.Bcc?.Count() ?? 0);
        }

        private static bool IsRetryableStatusCode(int statusCode)
        {
            return statusCode is 408 or 429 or 500 or 502 or 503 or 504;
        }

        private static int? GetRetryAfterSeconds(ApiException exception)
        {
            if (exception.ResponseHeaders == null)
            {
                return null;
            }

            var retryAfterHeader = exception.ResponseHeaders
                .FirstOrDefault(header => string.Equals(header.Key, "Retry-After", StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault();

            if (int.TryParse(retryAfterHeader, out var retryAfterSeconds))
            {
                return Math.Max(1, retryAfterSeconds);
            }

            if (DateTimeOffset.TryParse(retryAfterHeader, out var retryAfterAt))
            {
                return Math.Max(1, (int)Math.Ceiling((retryAfterAt - DateTimeOffset.UtcNow).TotalSeconds));
            }

            return null;
        }
    }
}
