using System.Text.Json;
using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.MagicLink.Events;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Service;

namespace Worker.Consumers.MagicLink
{
    /// <summary>
    /// Consumer that processes MagicLinkUsageEvent to track usage and handle expiration
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class MagicLinkUsageConsumer : IConsumer<MagicLinkUsageEvent>
    {
        private readonly ILogger<MagicLinkUsageConsumer> _logger;
        private readonly IMagicLinkRepository _repository;
        private readonly ICacheClient _cacheClient;

        public MagicLinkUsageConsumer(
            ILogger<MagicLinkUsageConsumer> logger,
            IMagicLinkRepository repository,
            ICacheClient cacheClient)
        {
            _logger = logger;
            _repository = repository;
            _cacheClient = cacheClient;
        }

        public async Task Consume(MagicLinkUsageEvent @event)
        {
            _logger.LogInformation("MagicLinkUsageConsumer: Processing usage event for LinkId={LinkId}",
                @event.LinkId);

            try
            {
                // Increment usage count in database and get updated document
                var updatedLink = await _repository.IncrementUsageCountAsync(@event.LinkId);

                if (updatedLink == null)
                {
                    _logger.LogWarning("MagicLinkUsageConsumer: MagicLink not found for LinkId={LinkId}",
                        @event.LinkId);
                    return;
                }

                // Store visitor usage data only after successful link lookup
                await StoreVisitorUsageAsync(@event);

                _logger.LogInformation("MagicLinkUsageConsumer: Updated usage count for LinkId={LinkId}, UsageCount={UsageCount}, UsageLimit={UsageLimit}",
                    @event.LinkId, updatedLink.UsageCount, updatedLink.UsageLimit);

                // Check if time-expired
                var isTimeExpired = updatedLink.ExpiryDate.HasValue && updatedLink.ExpiryDate.Value < DateTime.UtcNow;

                // Check if usage limit is reached (UsageLimit > 0 means limit is enabled)
                var isUsageLimitReached = updatedLink.UsageLimit > 0 && updatedLink.UsageCount >= updatedLink.UsageLimit;

                if (isTimeExpired)
                {
                    _logger.LogInformation("MagicLinkUsageConsumer: Time expired for LinkId={LinkId}. Marking as expired.",
                        @event.LinkId);

                    // Mark as expired in database
                    await _repository.MarkAsExpiredAsync(@event.LinkId, MagicLinkExpiredReason.TimeExpired);

                    // Remove from cache
                    await RemoveFromCacheIfExists(@event.LinkId);
                }
                else if (isUsageLimitReached)
                {
                    _logger.LogInformation("MagicLinkUsageConsumer: Usage limit reached for LinkId={LinkId}. Marking as expired.",
                        @event.LinkId);

                    // Mark as expired in database
                    await _repository.MarkAsExpiredAsync(@event.LinkId, MagicLinkExpiredReason.UsageLimitExceeded);

                    // Remove from cache
                    await RemoveFromCacheIfExists(@event.LinkId);
                }
                else
                {
                    // Update cache with new usage count
                    await UpdateCacheIfExists(updatedLink);
                }

                _logger.LogInformation("MagicLinkUsageConsumer: Successfully processed usage event for LinkId={LinkId}",
                    @event.LinkId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MagicLinkUsageConsumer: Error processing usage event for LinkId={LinkId}",
                    @event.LinkId);
            }
        }

        private async Task StoreVisitorUsageAsync(MagicLinkUsageEvent @event)
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
                    LinkType = MagicLinkType.Redirect.ToString(),
                    AccessedAt = @event.AccessedAt
                };

                await _repository.CreateVisitorUsageAsync(visitorUsage);
                _logger.LogInformation("MagicLinkUsageConsumer: Stored visitor usage for LinkId={LinkId}", @event.LinkId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MagicLinkUsageConsumer: Failed to store visitor usage for LinkId={LinkId}", @event.LinkId);
                // Don't throw - visitor usage storage failure shouldn't block the main flow
            }
        }

        private async Task RemoveFromCacheIfExists(string linkId)
        {
            var keyExists = await _cacheClient.KeyExistsAsync(linkId);
            if (keyExists)
            {
                await _cacheClient.RemoveKeyAsync(linkId);
                _logger.LogInformation("MagicLinkUsageConsumer: Removed LinkId={LinkId} from cache", linkId);
            }
        }

        private async Task UpdateCacheIfExists(Utility.DomainService.MagicLink.Models.MagicLink link)
        {
            var keyExists = await _cacheClient.KeyExistsAsync(link.ItemId);
            if (keyExists)
            {
                var cacheValue = new MagicLinkCacheValue
                {
                    ProjectKey = link.ProjectKey,
                    Type = link.Type.ToString()
                };
                var serializedData = JsonSerializer.Serialize(cacheValue);
                var ttl = link.Persistent ? 7 * 24 * 60 * 60 : 24 * 60 * 60;
                await _cacheClient.AddStringValueAsync(link.ItemId, serializedData, ttl);
                _logger.LogInformation("MagicLinkUsageConsumer: Updated cache for LinkId={LinkId}", link.ItemId);
            }
        }
    }

    /// <summary>
    /// Cache value structure for MagicLink
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class MagicLinkCacheValue
    {
        public string? ProjectKey { get; set; }
        public string? Type { get; set; }
    }
}

