using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Utility.DomainService.Shared.DTOs;
using Utility.DomainService.Shared.Services;

namespace Utility.DomainService.TemplateEngine.service
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class TemplateEngineNotificationService : ITemplateEngineNotificationService
    {
        private readonly ILogger<TemplateEngineNotificationService> _logger;
        private readonly ICryptoService _cryptoService;
        private readonly ITenants _tenants;
        private readonly IConfiguration _configuration;
        private readonly IHttpHelperServices _httpHelperServices;

        public TemplateEngineNotificationService(
            ILogger<TemplateEngineNotificationService> logger,
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

        public async Task NotifyRenderWithJsonEvent(bool success, string renderedFileId, string? subscriptionFilterId, string? projectKey)
        {
            _logger.LogInformation("NotifyRenderWithJsonEvent: Sending notification for renderedFileId={RenderedFileId}, success={Success}", renderedFileId, success);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyRenderWithJsonEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Template Render with JSON", success, subscriptionFilterId, new
            {
                RenderedFileId = renderedFileId,
                Success = success
            });
        }

        public async Task NotifyRenderWithJsonBulkEvent(bool success, string referenceId, string? subscriptionFilterId, string? projectKey, int successCount, int failureCount)
        {
            _logger.LogInformation("NotifyRenderWithJsonBulkEvent: Sending notification for referenceId={ReferenceId}, success={Success}", referenceId, success);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyRenderWithJsonBulkEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Template Render Bulk", success, subscriptionFilterId, new
            {
                ReferenceId = referenceId,
                SuccessCount = successCount,
                FailureCount = failureCount
            });
        }

        public async Task NotifyGenerateRenderedFileEvent(bool success, string fileId, string? subscriptionFilterId, string? projectKey)
        {
            _logger.LogInformation("NotifyGenerateRenderedFileEvent: Sending notification for fileId={FileId}, success={Success}", fileId, success);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyGenerateRenderedFileEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Generate Rendered File", success, subscriptionFilterId, new
            {
                FileId = fileId,
                Success = success
            });
        }

        public async Task NotifyGenerateRenderedFilesBulkEvent(bool success, string? bulkSubscriptionFilterId, string? projectKey, int successCount, int failureCount)
        {
            _logger.LogInformation("NotifyGenerateRenderedFilesBulkEvent: Sending notification, success={Success}", success);

            if (string.IsNullOrEmpty(bulkSubscriptionFilterId))
            {
                _logger.LogInformation("NotifyGenerateRenderedFilesBulkEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Generate Rendered Files Bulk", success, bulkSubscriptionFilterId, new
            {
                SuccessCount = successCount,
                FailureCount = failureCount
            });
        }

        public async Task NotifyCreateFileWithFilteredMongoQueryEvent(bool success, string fileId, string? subscriptionFilterId, string? projectKey)
        {
            _logger.LogInformation("NotifyCreateFileWithFilteredMongoQueryEvent: Sending notification for fileId={FileId}, success={Success}", fileId, success);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyCreateFileWithFilteredMongoQueryEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Create File with MongoDB Query", success, subscriptionFilterId, new
            {
                FileId = fileId,
                Success = success
            });
        }

        public async Task NotifyCreateFileWithFilteredMongoQueryBulkEvent(bool success, string? subscriptionFilterId, string? projectKey, int successCount, int failureCount)
        {
            _logger.LogInformation("NotifyCreateFileWithFilteredMongoQueryBulkEvent: Sending notification, success={Success}", success);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyCreateFileWithFilteredMongoQueryBulkEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Create Files with MongoDB Query Bulk", success, subscriptionFilterId, new
            {
                SuccessCount = successCount,
                FailureCount = failureCount
            });
        }

        public async Task NotifyCreateMultipleFileWithFilteredMongoQueryEvent(bool success, string requestId, string? subscriptionFilterId, string? projectKey, string message)
        {
            _logger.LogInformation("NotifyCreateMultipleFileWithFilteredMongoQueryEvent: Sending notification for requestId={RequestId}, success={Success}", requestId, success);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyCreateMultipleFileWithFilteredMongoQueryEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Create Multiple Files with MongoDB Query", success, subscriptionFilterId, new
            {
                RequestId = requestId,
                Success = success,
                Message = message
            });
        }

        private async Task SendNotificationAsync(string title, bool success, string subscriptionFilterId, object additionalData)
        {
            try
            {
                var requestData = new
                {
                    ConnectionId = subscriptionFilterId,
                    Roles = new List<string> { },
                    UserIds = new List<string> { BlocksContext.GetContext()?.UserId ?? "" },
                    DenormalizedPayload = JsonSerializer.Serialize(new
                    {
                        IsSuccess = success,
                        title = title,
                        description = success ? $"{title} completed successfully" : $"{title} failed",
                        data = additionalData
                    }),
                    SaveDenormalizedPayloadAsAnObject = false,
                    ConfiguratoinName = _configuration["BlocksAppNotificationReceiver"] ?? "template-engine",
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

