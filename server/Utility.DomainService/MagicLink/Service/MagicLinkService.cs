using System.Text.Json;
using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Utility.DomainService.MagicLink.Events;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Utilities;

namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Service implementation for magic link operations (unified UrlShortener + LinkToAction)
    /// </summary>
    public class MagicLinkService : IMagicLinkService
    {
        private readonly ILogger<MagicLinkService> _logger;
        private readonly IMagicLinkRepository _repository;
        private readonly ICacheClient _cacheClient;
        private readonly IMessageClient _messageClient;
        private readonly IConfiguration _configuration;
        private static readonly Random _random = new Random();
        private static readonly string _chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private const int DefaultLinkIdLength = 6;
        private const int MaxRetryAttempts = 10;

        public MagicLinkService(
            ILogger<MagicLinkService> logger,
            IMagicLinkRepository repository,
            ICacheClient cacheClient,
            IMessageClient messageClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _repository = repository;
            _cacheClient = cacheClient;
            _messageClient = messageClient;
            _configuration = configuration;
        }

        #region Create Operations

        public async Task<CreateMagicLinkResponse> CreateLinkAsync(CreateMagicLinkRequest request)
        {
            try
            {
                _logger.LogInformation("CreateLinkAsync started for Type: {Type}, Uri: {Uri}", request.Type, request.Uri);

                //var projectKey = request.ProjectKey ?? _configuration["RootTenantId"] ?? "";
                var projectKey = request.ProjectKey ?? "f080a1bea04280a72149fd689d50a48c" ?? "";

                // Get configuration if specified (for Action type)
                LinkBasedActionConfig? config = null;
                if (!string.IsNullOrEmpty(request.LinkBasedActionConfigId))
                {
                    config = await _repository.GetLinkConfigAsync(request.LinkBasedActionConfigId, projectKey);
                    if (config == null)
                    {
                        _logger.LogWarning("LinkBasedActionConfig not found: {ConfigId}", request.LinkBasedActionConfigId);
                    }
                }

                // Generate unique ID (with DB collision check)
                var linkId = await GenerateUniqueLinkIdAsync();

                // Build the MagicLink entity
                var magicLink = new Models.MagicLink
                {
                    ItemId = linkId,
                    Type = request.Type,
                    Name = request.Name,
                    Uri = request.Uri,
                    UriOnForbidden = request.UriOnForbidden,
                    RequestMethod = request.RequestMethod?.ToUpperInvariant(),
                    RequestPayload = request.RequestPayload,
                    RequestHeaders = request.RequestHeaders,
                    RequestEncodedQueryString = request.RequestEncodedQueryString,
                    RedirectUrl = request.RedirectUrl,
                    UsageLimit = request.UsageLimit,
                    UsageCount = 0,
                    ExpiryLifeSpan = request.ExpiryLifeSpan,
                    ExpiryDate = request.ExpiryLifeSpan > 0
                        ? DateTime.UtcNow.AddMilliseconds(request.ExpiryLifeSpan)
                        : null,
                    IsExpired = false,
                    ExpiredReason = null,
                    ProjectKey = projectKey,
                    ShortUri = BuildShortUri(linkId, config),
                    RequestByUserId = request.RequestByUserId,
                    UserCanLogin = request.UserCanLogin,
                    ClientCredential = request.ClientCredential,
                    LinkBasedActionConfigId = request.LinkBasedActionConfigId,
                    Persistent = request.Persistent,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = BlocksContext.GetContext()?.UserId
                };

                // Save to database
                await _repository.CreateMagicLinkAsync(magicLink);
                _logger.LogInformation("MagicLink saved to database: {LinkId}, Type: {Type}", linkId, request.Type);

                // Add to cache (Redis)
                await AddToCache(magicLink);

                _logger.LogInformation("CreateLinkAsync completed successfully: {LinkId}, ShortUri: {ShortUri}", linkId, magicLink.ShortUri);

                return new CreateMagicLinkResponse
                {
                    IsSuccess = true,
                    LinkId = linkId,
                    ShortUri = magicLink.ShortUri,
                    Type = request.Type.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateLinkAsync for Uri: {Uri}", request.Uri);
                return new CreateMagicLinkResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Error creating link: {ex.Message}"
                };
            }
        }

        public async Task<CreateMagicLinksResponse> CreateLinksAsync(CreateMagicLinksRequest request)
        {
            try
            {
                _logger.LogInformation("CreateLinksAsync started with {Count} requests", request.Requests.Count);

                var results = new List<MagicLinkResult>();
                var successCount = 0;

                foreach (var linkRequest in request.Requests)
                {
                    // Use the bulk request's ProjectKey if individual request doesn't have one
                    if (string.IsNullOrEmpty(linkRequest.ProjectKey))
                    {
                        linkRequest.ProjectKey = request.ProjectKey;
                    }

                    var result = await CreateLinkAsync(linkRequest);

                    results.Add(new MagicLinkResult
                    {
                        Id = result.LinkId,
                        ShortUri = result.ShortUri,
                        Type = result.Type,
                        IsSuccess = result.IsSuccess,
                        ErrorMessage = result.ErrorMessage
                    });

                    if (result.IsSuccess)
                    {
                        successCount++;
                    }
                }

                _logger.LogInformation("CreateLinksAsync completed: {SuccessCount}/{TotalCount} successful", successCount, request.Requests.Count);

                return new CreateMagicLinksResponse
                {
                    IsSuccess = successCount > 0,
                    Links = results,
                    TotalSuccessCount = successCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateLinksAsync");
                return new CreateMagicLinksResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Error creating links: {ex.Message}"
                };
            }
        }

        #endregion

        #region Remove Operations

        public async Task<RemoveMagicLinksResponse> RemoveLinksAsync(RemoveMagicLinksRequest request)
        {
            try
            {
                _logger.LogInformation("RemoveLinksAsync started with {Count} link IDs", request.LinkIds?.Count ?? 0);

                if (request.LinkIds == null || !request.LinkIds.Any())
                {
                    return new RemoveMagicLinksResponse
                    {
                        IsSuccess = true,
                        RemovedCount = 0
                    };
                }

                //var projectKey = request.ProjectKey ?? _configuration["RootTenantId"] ?? "";
                var projectKey = request.ProjectKey ?? "f080a1bea04280a72149fd689d50a48c" ?? "";
                var removedCount = 0;

                // Get the links from database
                var links = await _repository.GetMagicLinksByIdsAsync(request.LinkIds, projectKey);

                foreach (var link in links)
                {
                    try
                    {
                        // Remove from cache
                        var keyExists = await _cacheClient.KeyExistsAsync(link.ItemId);
                        if (keyExists)
                        {
                            await _cacheClient.RemoveKeyAsync(link.ItemId);
                            _logger.LogInformation("Removed link from cache: {LinkId}", link.ItemId);
                        }

                        // Update the link with removal information
                        link.IsExpired = true;
                        link.ExpiredReason = MagicLinkExpiredReason.ManuallyDisabled.ToString();
                        link.UpdatedAt = DateTime.UtcNow;
                        await _repository.UpdateMagicLinkAsync(link);

                        removedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error removing link: {LinkId}", link.ItemId);
                    }
                }

                _logger.LogInformation("RemoveLinksAsync completed: {RemovedCount} links removed", removedCount);

                return new RemoveMagicLinksResponse
                {
                    IsSuccess = true,
                    RemovedCount = removedCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RemoveLinksAsync");
                return new RemoveMagicLinksResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Error removing links: {ex.Message}"
                };
            }
        }

        #endregion

        #region Get Operations

        public async Task<GetMagicLinkResponse> GetLinkAsync(GetMagicLinkRequest request)
        {
            try
            {
                _logger.LogInformation("GetLinkAsync started for ItemId: {ItemId}, ProjectKey: {ProjectKey}",
                    request.ItemId, request.ProjectKey);

                var link = await _repository.GetMagicLinkAsync(request.ItemId, request.ProjectKey);

                if (link == null)
                {
                    _logger.LogWarning("GetLinkAsync: Link not found for ItemId: {ItemId}", request.ItemId);
                    return new GetMagicLinkResponse
                    {
                        Data = null,
                        IsSuccess = false,
                        ErrorMessage = "Link not found"
                    };
                }

                // Map entity to DTO with computed status
                var dto = MagicLinkDto.FromEntity(link);

                _logger.LogInformation("GetLinkAsync completed successfully for ItemId: {ItemId}", request.ItemId);

                return new GetMagicLinkResponse
                {
                    Data = dto,
                    IsSuccess = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetLinkAsync failed for ItemId: {ItemId}, ProjectKey: {ProjectKey}",
                    request.ItemId, request.ProjectKey);
                return new GetMagicLinkResponse
                {
                    Data = null,
                    IsSuccess = false,
                    ErrorMessage = $"Failed to get link: {ex.Message}"
                };
            }
        }

        public async Task<GetMagicLinksResponse> GetLinksAsync(GetMagicLinksRequest request)
        {
            try
            {
                _logger.LogInformation("GetLinksAsync started for ProjectKey: {ProjectKey}, Type: {Type}, PageSize: {PageSize}, PageNumber: {PageNumber}",
                    request.ProjectKey, request.Type, request.PageSize, request.PageNumber);

                var (links, totalCount) = await _repository.GetMagicLinksAsync(request);

                // Map entities to DTOs with computed status
                var dtos = links.Select(MagicLinkDto.FromEntity).ToList();

                _logger.LogInformation("GetLinksAsync completed. Retrieved {Count} links out of {Total}", dtos.Count, totalCount);

                return new GetMagicLinksResponse
                {
                    Data = dtos,
                    TotalCount = totalCount,
                    IsSuccess = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetLinksAsync failed for ProjectKey: {ProjectKey}", request.ProjectKey);
                return new GetMagicLinksResponse
                {
                    Data = new List<MagicLinkDto>(),
                    TotalCount = 0,
                    IsSuccess = false,
                    ErrorMessage = $"Failed to get links: {ex.Message}"
                };
            }
        }

        #endregion

        #region Invoke Operations

        public async Task<InvokeMagicLinkResponse> InvokeLinkAsync(InvokeMagicLinkRequest request)
        {
            try
            {
                _logger.LogInformation("InvokeLinkAsync started for LinkId: {LinkId}", request.LinkId);

                // Get the link
                var link = await _repository.GetMagicLinkAsync(request.LinkId, request.ProjectKey);

                if (link == null)
                {
                    _logger.LogWarning("InvokeLinkAsync: Link not found for LinkId: {LinkId}", request.LinkId);
                    return new InvokeMagicLinkResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "Link not found",
                        ErrorCode = "LINK_NOT_FOUND"
                    };
                }

                // Check if link is expired
                if (link.IsExpired)
                {
                    _logger.LogWarning("InvokeLinkAsync: Link is expired for LinkId: {LinkId}, Reason: {Reason}",
                        request.LinkId, link.ExpiredReason);
                    return new InvokeMagicLinkResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Link is expired: {link.ExpiredReason}",
                        ErrorCode = "LINK_EXPIRED"
                    };
                }

                // Check time-based expiry
                if (link.ExpiryDate.HasValue && link.ExpiryDate.Value < DateTime.UtcNow)
                {
                    _logger.LogWarning("InvokeLinkAsync: Link has time-expired for LinkId: {LinkId}", request.LinkId);
                    return new InvokeMagicLinkResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "Link has expired",
                        ErrorCode = "LINK_EXPIRED"
                    };
                }

                // Check usage limit
                if (link.UsageLimit > 0 && link.UsageCount >= link.UsageLimit)
                {
                    _logger.LogWarning("InvokeLinkAsync: Link usage limit exceeded for LinkId: {LinkId}", request.LinkId);
                    return new InvokeMagicLinkResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "Link usage limit exceeded",
                        ErrorCode = "LINK_LIMIT_EXCEEDED"
                    };
                }

                // Handle based on link type
                if (link.Type == MagicLinkType.Redirect)
                {
                    // Redirect type: send usage event and return the redirect URL
                    var usageEvent = new MagicLinkUsageEvent
                    {
                        LinkId = link.ItemId,
                        ProjectKey = link.ProjectKey,
                        AccessedAt = DateTime.UtcNow,
                        VisitorIpAddress = request.VisitorIpAddress,
                        VisitorUserAgent = request.VisitorUserAgent,
                        VisitorOrigin = request.VisitorOrigin,
                        VisitorLanguage = request.VisitorLanguage
                    };

                    // Fire and forget - don't wait for the event to be sent
                    _ = SendUsageEventAsync(usageEvent);

                    _logger.LogInformation("InvokeLinkAsync: Redirect type - returning redirect URL for LinkId: {LinkId}", request.LinkId);

                    return new InvokeMagicLinkResponse
                    {
                        IsSuccess = true,
                        RedirectUrl = link.Uri,
                        Type = MagicLinkType.Redirect.ToString()
                    };
                }
                else
                {
                    // Action type: queue action for background processing
                    var actionEvent = new MagicLinkActionEvent
                    {
                        LinkId = link.ItemId,
                        ProjectKey = link.ProjectKey,
                        SubscriptionFilterId = request.SubscriptionFilterId,
                        NotifyOnProcessEnding = request.NotifyOnProcessEnding,
                        RaiseEventOnProcessEnding = request.RaiseEventOnProcessEnding,
                        VisitorIpAddress = request.VisitorIpAddress,
                        VisitorUserAgent = request.VisitorUserAgent,
                        VisitorOrigin = request.VisitorOrigin,
                        VisitorLanguage = request.VisitorLanguage
                    };

                    await SendActionEventAsync(actionEvent);

                    _logger.LogInformation("InvokeLinkAsync: Action type - queued action for background processing for LinkId: {LinkId}", request.LinkId);

                    return new InvokeMagicLinkResponse
                    {
                        IsSuccess = true,
                        RedirectUrl = link.RedirectUrl, // Post-action redirect URL if configured
                        Type = MagicLinkType.Action.ToString()
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in InvokeLinkAsync for LinkId: {LinkId}", request.LinkId);
                return new InvokeMagicLinkResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Error: {ex.Message}"
                };
            }
        }

        #endregion

        #region Event Methods

        public async Task SendUsageEventAsync(MagicLinkUsageEvent usageEvent)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<MagicLinkUsageEvent>
                {
                    ConsumerName = MagicLinkConstants.MagicLinkUsageQueue,
                    Payload = usageEvent
                }
            );
        }

        public async Task SendActionEventAsync(MagicLinkActionEvent actionEvent)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<MagicLinkActionEvent>
                {
                    ConsumerName = MagicLinkConstants.MagicLinkActionQueue,
                    Payload = actionEvent
                }
            );
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Generates a unique link ID with database collision check.
        /// Retries up to MaxRetryAttempts times if a collision is detected.
        /// </summary>
        /// <param name="length">Length of the generated ID (default: 6)</param>
        /// <returns>A unique link ID</returns>
        private async Task<string> GenerateUniqueLinkIdAsync(int length = DefaultLinkIdLength)
        {
            for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                var linkId = GenerateLinkId(length);
                
                // Check if this ID already exists in database
                var existingLink = await _repository.GetMagicLinkAsync(linkId);
                
                if (existingLink == null)
                {
                    _logger.LogDebug("Generated unique LinkId: {LinkId} on attempt {Attempt}", linkId, attempt + 1);
                    return linkId;
                }
                
                _logger.LogWarning("LinkId collision detected: {LinkId}, retrying (attempt {Attempt}/{MaxAttempts})", 
                    linkId, attempt + 1, MaxRetryAttempts);
            }
            
            // If all retries failed, generate a longer ID as fallback
            _logger.LogWarning("Max retry attempts reached for LinkId generation, using extended length");
            return GenerateLinkId(length + 4);
        }

        /// <summary>
        /// Generates a random link ID using only letters (uppercase and lowercase).
        /// </summary>
        /// <param name="length">Length of the generated ID</param>
        /// <returns>A random link ID</returns>
        private static string GenerateLinkId(int length = DefaultLinkIdLength)
        {
            return new string(Enumerable.Repeat(_chars, length)
                .Select(s => s[_random.Next(s.Length)]).ToArray());
        }

        private string BuildShortUri(string linkId, LinkBasedActionConfig? config)
        {
            var baseUrl = config?.ShortUrlBase?.TrimEnd('/')
                ?? _configuration["MagicLinkBaseAddress"]?.TrimEnd('/')
                // ?? _configuration["ShortUrlBaseAddress"]?.TrimEnd('/');
                ?? "https://dev-short.seliseblocks.com";

            return $"{baseUrl}/{linkId}";
        }

        private async Task AddToCache(Models.MagicLink link)
        {
            // Cache the link data for quick access
            var cacheValue = new MagicLinkCacheValue
            {
                ProjectKey = link.ProjectKey,
                Type = link.Type.ToString()
            };
            var serializedValue = JsonSerializer.Serialize(cacheValue);

            if (link.ExpiryLifeSpan > 0)
            {
                // Add with expiry (convert milliseconds to seconds)
                var expirySeconds = (int)(link.ExpiryLifeSpan / 1000);
                await _cacheClient.AddStringValueAsync(link.ItemId, serializedValue, expirySeconds);
                _logger.LogInformation("LinkId added to cache with expiry: {LinkId}, Expiry: {Expiry}s", link.ItemId, expirySeconds);
            }
            else if (link.Persistent)
            {
                // Persistent links get a longer TTL (1 year)
                await _cacheClient.AddStringValueAsync(link.ItemId, serializedValue, 365 * 24 * 60 * 60);
                _logger.LogInformation("LinkId added to cache as persistent: {LinkId}", link.ItemId);
            }
            else
            {
                // Non-persistent links get a shorter TTL (7 days)
                await _cacheClient.AddStringValueAsync(link.ItemId, serializedValue, 7 * 24 * 60 * 60);
                _logger.LogInformation("LinkId added to cache without expiry: {LinkId}", link.ItemId);
            }
        }

        #endregion

        #region LinkBasedActionConfig Operations

        public async Task<SaveLinkBasedActionConfigResponse> SaveLinkBasedActionConfigAsync(SaveLinkBasedActionConfigRequest request)
        {
            try
            {
                //var projectKey = request.ProjectKey ?? _configuration["RootTenantId"] ?? "";
                var projectKey = request.ProjectKey ?? "f080a1bea04280a72149fd689d50a48c" ?? "";
                _logger.LogInformation("SaveLinkBasedActionConfigAsync started for ProjectKey: {ProjectKey}", projectKey);

                // Check if config already exists for this project
                var existingConfig = await _repository.GetLinkBasedActionConfigAsync(projectKey);

                if (existingConfig == null)
                {
                    // Create new config
                    var newConfig = new Models.LinkBasedActionConfig
                    {
                        ItemId = Guid.NewGuid().ToString(),
                        ContextName = request.ContextName,
                        ShortUrlBase = request.ShortUrlBase,
                        ProjectKey = projectKey,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _repository.CreateLinkBasedActionConfigAsync(newConfig);
                    _logger.LogInformation("LinkBasedActionConfig created: {ConfigId}", newConfig.ItemId);

                    return new SaveLinkBasedActionConfigResponse
                    {
                        IsSuccess = true,
                        ConfigId = newConfig.ItemId,
                        WasCreated = true,
                        Config = newConfig
                    };
                }
                else
                {
                    // Update existing config
                    existingConfig.ContextName = request.ContextName;
                    existingConfig.ShortUrlBase = request.ShortUrlBase;
                    existingConfig.UpdatedAt = DateTime.UtcNow;

                    var updated = await _repository.UpdateLinkBasedActionConfigAsync(existingConfig);
                    
                    if (!updated)
                    {
                        _logger.LogWarning("Failed to update LinkBasedActionConfig: {ConfigId}", existingConfig.ItemId);
                        return new SaveLinkBasedActionConfigResponse
                        {
                            IsSuccess = false,
                            ErrorMessage = "Failed to update configuration"
                        };
                    }

                    _logger.LogInformation("LinkBasedActionConfig updated: {ConfigId}", existingConfig.ItemId);

                    return new SaveLinkBasedActionConfigResponse
                    {
                        IsSuccess = true,
                        ConfigId = existingConfig.ItemId,
                        WasCreated = false,
                        Config = existingConfig
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SaveLinkBasedActionConfigAsync for ProjectKey: {ProjectKey}", request.ProjectKey);
                return new SaveLinkBasedActionConfigResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Error saving configuration: {ex.Message}"
                };
            }
        }

        public async Task<GetLinkBasedActionConfigResponse> GetLinkBasedActionConfigAsync(GetLinkBasedActionConfigRequest request)
        {
            try
            {
                //var projectKey = request.ProjectKey ?? _configuration["RootTenantId"] ?? "";
                var projectKey = request.ProjectKey ?? "f080a1bea04280a72149fd689d50a48c" ?? "";
                _logger.LogInformation("GetLinkBasedActionConfigAsync started for ProjectKey: {ProjectKey}", projectKey);

                var config = await _repository.GetLinkBasedActionConfigAsync(projectKey);

                return new GetLinkBasedActionConfigResponse
                {
                    IsSuccess = true,
                    Config = config
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetLinkBasedActionConfigAsync for ProjectKey: {ProjectKey}", request.ProjectKey);
                return new GetLinkBasedActionConfigResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Error getting configuration: {ex.Message}"
                };
            }
        }

        #endregion
    }

    /// <summary>
    /// Value stored in cache for magic links
    /// </summary>
    public class MagicLinkCacheValue
    {
        public string? ProjectKey { get; set; }
        public string? Type { get; set; }
    }
}

