using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.MagicLink.Events;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Service;

namespace Worker.Consumers.MagicLink
{
    /// <summary>
    /// Consumer that processes MagicLinkActionEvent to execute action-type magic links
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class MagicLinkActionConsumer : IConsumer<MagicLinkActionEvent>
    {
        private readonly ILogger<MagicLinkActionConsumer> _logger;
        private readonly IMagicLinkRepository _repository;
        private readonly ICacheClient _cacheClient;
        private readonly MagicLinkActionExecutor _actionExecutor;
        private readonly IMagicLinkNotificationService _notificationService;
        private readonly IClientCredentialTokenService _tokenService;

        public MagicLinkActionConsumer(
            ILogger<MagicLinkActionConsumer> logger,
            IMagicLinkRepository repository,
            ICacheClient cacheClient,
            MagicLinkActionExecutor actionExecutor,
            IMagicLinkNotificationService notificationService,
            IClientCredentialTokenService tokenService)
        {
            _logger = logger;
            _repository = repository;
            _cacheClient = cacheClient;
            _actionExecutor = actionExecutor;
            _notificationService = notificationService;
            _tokenService = tokenService;
        }

        public async Task Consume(MagicLinkActionEvent @event)
        {
            _logger.LogInformation("MagicLinkActionConsumer: Processing action event for LinkId={LinkId}",
                @event.LinkId);

            try
            {
                // Check if link exists in cache
                var linkExists = await _cacheClient.KeyExistsAsync(@event.LinkId);

                if (linkExists)
                {
                    await ProcessActiveLink(@event);
                }
                else
                {
                    await ProcessExpiredOrRemovedLink(@event);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MagicLinkActionConsumer: Exception occurred while processing action event for LinkId={LinkId}",
                    @event.LinkId);

                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyActionExecutedEvent(
                        false,
                        @event.LinkId,
                        500,
                        ex.Message,
                        @event.SubscriptionFilterId,
                        @event.ProjectKey);
                }
            }
        }

        private async Task ProcessActiveLink(MagicLinkActionEvent @event)
        {
            _logger.LogInformation("MagicLinkActionConsumer: Link exists in cache, processing action for LinkId={LinkId}", @event.LinkId);

            // Get the MagicLink from database
            var link = await _repository.GetMagicLinkAsync(@event.LinkId);

            if (link == null)
            {
                _logger.LogError("MagicLinkActionConsumer: MagicLink not found in database for LinkId={LinkId}", @event.LinkId);

                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyActionExecutedEvent(
                        false,
                        @event.LinkId,
                        404,
                        "Link not found",
                        @event.SubscriptionFilterId,
                        @event.ProjectKey);
                }
                return;
            }

            // Verify this is an Action type link
            if (link.Type != MagicLinkType.Action)
            {
                _logger.LogWarning("MagicLinkActionConsumer: Link is not an Action type for LinkId={LinkId}", @event.LinkId);

                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyActionExecutedEvent(
                        false,
                        @event.LinkId,
                        400,
                        "Link is not an Action type",
                        @event.SubscriptionFilterId,
                        @event.ProjectKey);
                }
                return;
            }

            var projectKey = link.ProjectKey;

            // Get token using ClientCredential if available
            string? token = null;
            if (!string.IsNullOrEmpty(link.ClientCredential))
            {
                _logger.LogInformation("MagicLinkActionConsumer: ClientCredential found, attempting to get token for LinkId={LinkId}",
                    @event.LinkId);

                var clientCredentials = await _repository.GetClientCredentialsAsync(link.ClientCredential, link.ProjectKey);

                if (clientCredentials != null)
                {
                    if (clientCredentials.IsActive)
                    {
                        token = await _tokenService.GetTokenAsync(clientCredentials, projectKey);

                        if (string.IsNullOrEmpty(token))
                        {
                            _logger.LogWarning("MagicLinkActionConsumer: Failed to obtain token for ClientCredential={ClientCredential}, LinkId={LinkId}. Proceeding without token.",
                                link.ClientCredential, @event.LinkId);
                        }
                        else
                        {
                            _logger.LogInformation("MagicLinkActionConsumer: Successfully obtained token for LinkId={LinkId}",
                                @event.LinkId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("MagicLinkActionConsumer: ClientCredential is inactive for ClientCredential={ClientCredential}, LinkId={LinkId}",
                            link.ClientCredential, @event.LinkId);
                    }
                }
                else
                {
                    _logger.LogWarning("MagicLinkActionConsumer: ClientCredential not found in database for ClientCredential={ClientCredential}, LinkId={LinkId}",
                        link.ClientCredential, @event.LinkId);
                }
            }
            else
            {
                _logger.LogInformation("MagicLinkActionConsumer: No ClientCredential configured, executing action without token for LinkId={LinkId}",
                    @event.LinkId);
            }

            // Execute the action
            var result = await _actionExecutor.ExecuteActionAsync(link, token);

            _logger.LogInformation("MagicLinkActionConsumer: Action executed for LinkId={LinkId}, Success={Success}, StatusCode={StatusCode}",
                @event.LinkId, result.IsSuccess, result.StatusCode);

            // Store visitor usage only on successful action execution
            await StoreVisitorUsageAsync(@event, result.IsSuccess, result.StatusCode, result.ErrorMessage);

            // Perform after action process (usage tracking and expiration)
            var linkRemoved = await PerformAfterActionProcess(link);

            // Send notification if requested
            if (@event.NotifyOnProcessEnding)
            {
                await _notificationService.NotifyActionExecutedEvent(
                    result.IsSuccess,
                    @event.LinkId,
                    result.StatusCode,
                    result.ErrorMessage,
                    @event.SubscriptionFilterId,
                    projectKey);
            }

            _logger.LogInformation("MagicLinkActionConsumer: Successfully processed action event for LinkId={LinkId}, LinkRemoved={LinkRemoved}",
                @event.LinkId, linkRemoved);
        }

        private async Task ProcessExpiredOrRemovedLink(MagicLinkActionEvent @event)
        {
            _logger.LogInformation("MagicLinkActionConsumer: Link not found in cache (expired/removed) for LinkId={LinkId}", @event.LinkId);

            // Get the link from database to check removal reason
            var link = await _repository.GetMagicLinkAsync(@event.LinkId);

            string errorCode;
            string errorMessage;

            if (link != null && link.IsExpired)
            {
                errorCode = "LINK_EXPIRED";
                errorMessage = link.ExpiredReason ?? "Link has expired";
            }
            else if (link != null)
            {
                // Check if time-expired even though not marked
                if (link.ExpiryDate.HasValue && link.ExpiryDate.Value < DateTime.UtcNow)
                {
                    errorCode = "LINK_EXPIRED";
                    errorMessage = MagicLinkExpiredReason.TimeExpired.ToString();
                }
                else if (link.UsageLimit > 0 && link.UsageCount >= link.UsageLimit)
                {
                    errorCode = "LINK_LIMIT_EXCEEDED";
                    errorMessage = MagicLinkExpiredReason.UsageLimitExceeded.ToString();
                }
                else
                {
                    errorCode = "LINK_LIFESPAN_EXPIRED";
                    errorMessage = MagicLinkExpiredReason.LifespanExpired.ToString();
                }
            }
            else
            {
                errorCode = "LINK_NOT_FOUND";
                errorMessage = "Link not found";
            }

            _logger.LogInformation("MagicLinkActionConsumer: Link expired/removed for LinkId={LinkId}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}",
                @event.LinkId, errorCode, errorMessage);

            // No visitor usage stored for failed access attempts

            if (@event.NotifyOnProcessEnding)
            {
                await _notificationService.NotifyActionExecutedEvent(
                    false,
                    @event.LinkId,
                    404,
                    errorMessage,
                    @event.SubscriptionFilterId,
                    @event.ProjectKey);
            }
        }

        private async Task StoreVisitorUsageAsync(MagicLinkActionEvent @event, bool? actionSuccess, int? statusCode, string? errorMessage)
        {
            try
            {
                var visitorUsage = new MagicLinkVisitorUsage
                {
                    LinkId = @event.LinkId,
                    ProjectKey = @event.ProjectKey,
                    VisitorIpAddress = @event.VisitorIpAddress,
                    VisitorUserAgent = @event.VisitorUserAgent,
                    VisitorOrigin = @event.VisitorOrigin,
                    VisitorLanguage = @event.VisitorLanguage,
                    LinkType = MagicLinkType.Action.ToString(),
                    AccessedAt = DateTime.UtcNow,
                    ActionSuccess = actionSuccess,
                    ActionStatusCode = statusCode,
                    ActionErrorMessage = errorMessage
                };

                await _repository.CreateVisitorUsageAsync(visitorUsage);
                _logger.LogInformation("MagicLinkActionConsumer: Stored visitor usage for LinkId={LinkId}", @event.LinkId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MagicLinkActionConsumer: Failed to store visitor usage for LinkId={LinkId}", @event.LinkId);
                // Don't throw - visitor usage storage failure shouldn't block the main flow
            }
        }

        private async Task<bool> PerformAfterActionProcess(Utility.DomainService.MagicLink.Models.MagicLink link)
        {
            var linkRemoved = false;

            // Increment usage count
            link.UsageCount++;

            // Check if usage limit is reached
            if (link.UsageLimit > 0 && link.UsageCount >= link.UsageLimit)
            {
                // Remove from cache
                await _cacheClient.RemoveKeyAsync(link.ItemId);

                // Mark as expired
                link.IsExpired = true;
                link.ExpiredReason = MagicLinkExpiredReason.UsageLimitExceeded.ToString();

                linkRemoved = true;
                _logger.LogInformation("MagicLinkActionConsumer: Link removed due to limit exceeded for LinkId={LinkId}", link.ItemId);
            }

            // Always update the link in database to persist UsageCount
            await _repository.UpdateMagicLinkAsync(link);

            return linkRemoved;
        }
    }
}

