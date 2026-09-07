using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Utility.DomainService.Shared.DTOs;
using Utility.DomainService.Shared.Utilities;
using Utility.DomainService.Shared.Services;

namespace Utility.DomainService.PdfGenerator.service
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfGeneratorNotificationService : IPdfGeneratorNotificationService
    {
        private readonly ILogger<PdfGeneratorNotificationService> _logger;
        private readonly ICryptoService _cryptoService;
        private readonly ITenants _tenants;
        private readonly IConfiguration _configuration;
        private readonly IHttpHelperServices _httpHelperServices;

        public PdfGeneratorNotificationService(
            ILogger<PdfGeneratorNotificationService> logger,
            ICryptoService cryptoService,
            ITenants tenants,
            IConfiguration configuration,
            IHttpHelperServices httpHelperServices)
        {
            _logger = logger;
            _cryptoService = cryptoService;
            _tenants = tenants;
            _configuration = configuration;
            _httpHelperServices = httpHelperServices;
        }

        public async Task NotifyMergePdfsEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? projectKey)
        {
            _logger.LogInformation("NotifyMergePdfsEvent: Sending notification for outputPdfFileId={OutputPdfFileId}, success={Success}", LogSanitizer.Scrub(outputPdfFileId), success);

            await SendNotificationAsync("PDF Merge", success, messageCoRelationId, new
            {
                OutputPdfFileId = outputPdfFileId,
                MessageCoRelationId = messageCoRelationId,
                Success = success
            });
        }

        public async Task NotifyCreatePdfsFromHtmlEvent(bool success, string messageCoRelationId, string? projectKey, int successCount, int failureCount)
        {
            _logger.LogInformation("NotifyCreatePdfsFromHtmlEvent: Sending notification for messageCoRelationId={MessageCoRelationId}, success={Success}", LogSanitizer.Scrub(messageCoRelationId), success);

            await SendNotificationAsync("Create PDFs from HTML", success, messageCoRelationId, new
            {
                MessageCoRelationId = messageCoRelationId,
                SuccessCount = successCount,
                FailureCount = failureCount
            });
        }

        public async Task NotifyExtractTextFromPdfsEvent(bool success, string messageCoRelationId, string? projectKey)
        {
            _logger.LogInformation("NotifyExtractTextFromPdfsEvent: Sending notification for messageCoRelationId={MessageCoRelationId}, success={Success}", LogSanitizer.Scrub(messageCoRelationId), success);

            await SendNotificationAsync("Extract Text from PDFs", success, messageCoRelationId, new
            {
                MessageCoRelationId = messageCoRelationId,
                Success = success
            });
        }

        public async Task NotifyCreatePdfsFromHtmlUsingTEEvent(bool success, string messageCoRelationId, string? projectKey)
        {
            _logger.LogInformation("NotifyCreatePdfsFromHtmlUsingTEEvent: Sending notification for messageCoRelationId={MessageCoRelationId}, success={Success}", LogSanitizer.Scrub(messageCoRelationId), success);

            await SendNotificationAsync("Create PDFs from HTML using Template Engine", success, messageCoRelationId, new
            {
                MessageCoRelationId = messageCoRelationId,
                Success = success
            });
        }

        public async Task NotifyCreatePdfsFromHtmlUsingTEBulkEvent(bool success, string messageCoRelationId, string? projectKey, int successCount, int failureCount)
        {
            _logger.LogInformation("NotifyCreatePdfsFromHtmlUsingTEBulkEvent: Sending notification for messageCoRelationId={MessageCoRelationId}, success={Success}", LogSanitizer.Scrub(messageCoRelationId), success);

            await SendNotificationAsync("Create PDFs from HTML using Template Engine Bulk", success, messageCoRelationId, new
            {
                MessageCoRelationId = messageCoRelationId,
                SuccessCount = successCount,
                FailureCount = failureCount
            });
        }

        public async Task NotifyFixPdfsEvent(bool success, string messageCorrelationId, string? projectKey)
        {
            _logger.LogInformation("NotifyFixPdfsEvent: Sending notification for messageCorrelationId={MessageCorrelationId}, success={Success}", LogSanitizer.Scrub(messageCorrelationId), success);

            await SendNotificationAsync("Fix PDFs", success, messageCorrelationId, new
            {
                MessageCorrelationId = messageCorrelationId,
                Success = success
            });
        }

        public async Task NotifyStampImageToPdfEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? projectKey)
        {
            _logger.LogInformation("NotifyStampImageToPdfEvent: Sending notification for outputPdfFileId={OutputPdfFileId}, success={Success}", LogSanitizer.Scrub(outputPdfFileId), success);

            await SendNotificationAsync("Stamp Image to PDF", success, messageCoRelationId, new
            {
                OutputPdfFileId = outputPdfFileId,
                MessageCoRelationId = messageCoRelationId,
                Success = success
            });
        }

        public async Task NotifyStampTextToPdfEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? projectKey)
        {
            _logger.LogInformation("NotifyStampTextToPdfEvent: Sending notification for outputPdfFileId={OutputPdfFileId}, success={Success}", LogSanitizer.Scrub(outputPdfFileId), success);

            await SendNotificationAsync("Stamp Text to PDF", success, messageCoRelationId, new
            {
                OutputPdfFileId = outputPdfFileId,
                MessageCoRelationId = messageCoRelationId,
                Success = success
            });
        }

        public async Task NotifyStampIntoPdfEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? projectKey)
        {
            _logger.LogInformation("NotifyStampIntoPdfEvent: Sending notification for outputPdfFileId={OutputPdfFileId}, success={Success}", LogSanitizer.Scrub(outputPdfFileId), success);

            await SendNotificationAsync("Stamp into PDF", success, messageCoRelationId, new
            {
                OutputPdfFileId = outputPdfFileId,
                MessageCoRelationId = messageCoRelationId,
                Success = success
            });
        }

        public async Task NotifyConvertDocumentToPdfEvent(bool success, string fileId, string messageCoRelationId, string? projectKey)
        {
            _logger.LogInformation("NotifyConvertDocumentToPdfEvent: Sending notification for fileId={FileId}, success={Success}", LogSanitizer.Scrub(fileId), success);

            await SendUserNotificationAsync(success, messageCoRelationId, new
            {
                FileId = fileId,
                MessageCoRelationId = messageCoRelationId,
                Success = success
            });
        }

        /// <summary>
        /// Sends a document-conversion notification to <c>POST /api/Notifier/Notify</c>, targeted
        /// at the requesting user rather than a push connection.
        /// </summary>
        /// <remarks>
        /// The other Notify* methods target a <c>connectionId</c> (<see cref="SendNotificationAsync"/>
        /// sets it from <c>subscriptionFilterId</c>, i.e. the caller's correlation id) and skip
        /// sending entirely when the caller supplied none, because there is nothing to push to
        /// otherwise. Document conversion targets <c>userIds</c> instead — the ambient
        /// <see cref="BlocksContext"/>'s user, the same identity every other notification here
        /// already includes — so there is a valid target as long as that identity resolves, and the
        /// gate on the correlation id no longer applies: an empty one means only that the client has
        /// no way to correlate the push with its original request, not that there is nowhere to
        /// send it.
        /// <para>
        /// <c>connectionId</c> is left unset (the real request schema, <c>NotifyRequest</c>, allows
        /// it to be null) rather than reused for anything else, and <c>configurationName</c> is the
        /// fixed <c>"SignatureNotification"</c> rather than the configurable/defaulted value the
        /// other methods use — both per the notification service's own contract for this
        /// notification type, not something derived from configuration.
        /// </para>
        /// </remarks>
        private async Task SendUserNotificationAsync(bool success, string? messageCoRelationId, object payload)
        {
            try
            {
                var userId = BlocksContext.GetContext()?.UserId;
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogInformation(
                        "SendUserNotificationAsync: No authenticated user in context, skipping notification");
                    return;
                }

                var requestData = new
                {
                    UserIds = new List<string> { userId },
                    DenormalizedPayload = JsonSerializer.Serialize(payload),
                    SaveDenormalizedPayloadAsAnObject = false,
                    ConfigurationName = "SignatureNotification",
                    ContentAvailable = true,
                    ResponseKey = string.IsNullOrEmpty(messageCoRelationId) ? null : messageCoRelationId,
                    ResponseValue = success.ToString()
                };

                var rootTenantId = _configuration["RootTenantId"];
                var salt = _tenants.GetTenantByID(rootTenantId)?.TenantSalt;
                var actualSecret = _cryptoService.Hash(rootTenantId, salt);

                var url = _configuration["NotificationServiceUrl"];
                var headers = new Dictionary<string, string>
                {
                    { "x-blocks-key", rootTenantId },
                    { "Secret", actualSecret }
                };

                var (result, _) = await _httpHelperServices.MakeHttpPostRequest<NotificationResponse>(
                    requestData, url, headers);

                if (result != null && result.isSuccess)
                {
                    _logger.LogInformation("SendUserNotificationAsync: Notification sent successfully for userId={UserId}", LogSanitizer.Scrub(userId));
                }
                else
                {
                    _logger.LogWarning("SendUserNotificationAsync: Failed to send notification. Error: {Errors}", result?.errors);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendUserNotificationAsync: Error sending notification");
            }
        }

        private async Task SendNotificationAsync(string title, bool success, string subscriptionFilterId, object additionalData)
        {
            try
            {
                if (string.IsNullOrEmpty(subscriptionFilterId))
                {
                    _logger.LogInformation("{Title}: No subscriptionFilterId provided, skipping notification", title);
                    return;
                }

                var requestData = new
                {
                    ConnectionId = subscriptionFilterId,
                    Roles = new List<string> { },
                    UserIds = new List<string> { BlocksContext.GetContext()?.UserId ?? "" },
                    DenormalizedPayload = JsonSerializer.Serialize(new
                    {
                        IsSuccess = success,
                        Title = title,
                        Description = success ? $"{title} completed successfully" : $"{title} failed",
                        Data = additionalData
                    }),
                    SaveDenormalizedPayloadAsAnObject = false,
                    ConfigurationName = _configuration["BlocksAppNotificationReceiver"] ?? "pdf-generator",
                    ContentAvailable = true,
                    ResponseKey = subscriptionFilterId,
                    ResponseValue = success.ToString()
                };

                var blocksKey = _configuration["RootTenantId"];
                var rootTenantId = _configuration["RootTenantId"];
                var salt = _tenants.GetTenantByID(rootTenantId)?.TenantSalt;
                var actualSecret = _cryptoService.Hash(rootTenantId, salt);

                var url = _configuration["NotificationServiceUrl"];
                var headers = new Dictionary<string, string>
                {
                    { "x-blocks-key", blocksKey },
                    { "Secret", actualSecret }
                };

                var (result, _) = await _httpHelperServices.MakeHttpPostRequest<NotificationResponse>(
                    requestData, url, headers);

                if (result != null && result.isSuccess)
                {
                    _logger.LogInformation("Notification sent successfully for: {Title}", title);
                }
                else
                {
                    _logger.LogWarning("Failed to send notification for: {Title}. Error: {Errors}", title, result?.errors);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notification for: {Title}", title);
            }
        }
    }
}

