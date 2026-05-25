using System.Text.Json;
using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Utility.DomainService.Shared.DTOs;
using Utility.DomainService.Shared.Services;

namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Notification service implementation for magic link operations
    /// </summary>
    public class MagicLinkNotificationService : IMagicLinkNotificationService
    {
        private readonly ILogger<MagicLinkNotificationService> _logger;
        private readonly ICryptoService _cryptoService;
        private readonly ITenants _tenants;
        private readonly IConfiguration _configuration;
        private readonly IHttpHelperServices _httpHelperServices;

        public MagicLinkNotificationService(
            ILogger<MagicLinkNotificationService> logger,
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

        public async Task NotifyLinkCreatedEvent(bool success, string linkId, string shortUri, string? subscriptionFilterId, string? projectKey)
        {
            _logger.LogInformation("NotifyLinkCreatedEvent: Sending notification for linkId={LinkId}, success={Success}", linkId, success);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyLinkCreatedEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Magic Link Created", success, subscriptionFilterId, new
            {
                LinkId = linkId,
                ShortUri = shortUri,
                Success = success
            });
        }

        public async Task NotifyLinksCreatedEvent(bool success, int successCount, int failureCount, string? subscriptionFilterId, string? projectKey)
        {
            _logger.LogInformation("NotifyLinksCreatedEvent: Sending notification, success={Success}, successCount={SuccessCount}", success, successCount);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyLinksCreatedEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Magic Links Created Bulk", success, subscriptionFilterId, new
            {
                SuccessCount = successCount,
                FailureCount = failureCount
            });
        }

        public async Task NotifyLinksRemovedEvent(bool success, int removedCount, string? subscriptionFilterId, string? projectKey)
        {
            _logger.LogInformation("NotifyLinksRemovedEvent: Sending notification, success={Success}, removedCount={RemovedCount}", success, removedCount);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyLinksRemovedEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Magic Links Removed", success, subscriptionFilterId, new
            {
                RemovedCount = removedCount
            });
        }

        public async Task NotifyActionExecutedEvent(bool success, string linkId, int statusCode, string? errorMessage, string? subscriptionFilterId, string? projectKey)
        {
            _logger.LogInformation("NotifyActionExecutedEvent: Sending notification for linkId={LinkId}, success={Success}, statusCode={StatusCode}", linkId, success, statusCode);

            if (string.IsNullOrEmpty(subscriptionFilterId))
            {
                _logger.LogInformation("NotifyActionExecutedEvent: No subscriptionFilterId provided, skipping notification");
                return;
            }

            await SendNotificationAsync("Magic Link Action Executed", success, subscriptionFilterId, new
            {
                LinkId = linkId,
                StatusCode = statusCode,
                ErrorMessage = errorMessage,
                Success = success
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
                    ConfiguratoinName = _configuration["BlocksAppNotificationReceiver"] ?? "magic-link",
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

