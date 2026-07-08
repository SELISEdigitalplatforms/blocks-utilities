using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Sms.DomainService.Entities;
using StackExchange.Redis;

namespace Sms.DomainService.Services;

public class SmsRateLimiter : ISmsRateLimiter
{
    private const string KeyPrefix = "sms:rate-limit";

    private readonly ICacheClient _cacheClient;
    private readonly ILogger<SmsRateLimiter> _logger;

    public SmsRateLimiter(ICacheClient cacheClient, ILogger<SmsRateLimiter> logger)
    {
        _cacheClient = cacheClient;
        _logger = logger;
    }

    public async Task<SmsRateLimitResult> CheckAsync(SmsMessage message, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var windowSeconds = Math.Max(1, configuration.RateLimitWindowSeconds);
        var max = Math.Max(1, configuration.RateLimitMaxPerWindow);

        try
        {
            var cache = _cacheClient.CacheDatabase();
            var tenantKey = BuildTenantKey(message.ProjectKey, message.TenantId, windowSeconds);
            if (!await TryConsumeAsync(cache, tenantKey, max, windowSeconds).ConfigureAwait(false))
            {
                _logger.LogWarning("SMS tenant rate limit exceeded TenantId={TenantId}, ProjectKey={ProjectKey}, WindowSeconds={WindowSeconds}, Max={Max}",
                    message.TenantId, message.ProjectKey, windowSeconds, max);
                return SmsRateLimitResult.Blocked("Tenant SMS rate limit exceeded.");
            }

            foreach (var destination in message.DestinationNumbers.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var recipientKey = BuildRecipientKey(message.ProjectKey, message.TenantId, destination, windowSeconds);
                if (!await TryConsumeAsync(cache, recipientKey, max, windowSeconds).ConfigureAwait(false))
                {
                    _logger.LogWarning("SMS recipient rate limit exceeded TenantId={TenantId}, ProjectKey={ProjectKey}, RecipientHash={RecipientHash}, WindowSeconds={WindowSeconds}, Max={Max}",
                        message.TenantId, message.ProjectKey, Hash(destination), windowSeconds, max);
                    return SmsRateLimitResult.Blocked("Recipient SMS rate limit exceeded.");
                }
            }

            return SmsRateLimitResult.Allowed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS rate limiter failed closed TenantId={TenantId}, ProjectKey={ProjectKey}", message.TenantId, message.ProjectKey);
            return SmsRateLimitResult.Blocked("SMS rate limiter is unavailable.");
        }
    }

    private static async Task<bool> TryConsumeAsync(IDatabase cache, string key, int max, int windowSeconds)
    {
        var count = await cache.StringIncrementAsync(key).ConfigureAwait(false);
        if (count == 1)
        {
            await cache.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds)).ConfigureAwait(false);
        }

        return count <= max;
    }

    private static string BuildTenantKey(string projectKey, string tenantId, int windowSeconds)
    {
        return $"{KeyPrefix}:tenant:{Normalize(projectKey)}:{Normalize(tenantId)}:{windowSeconds}";
    }

    private static string BuildRecipientKey(string projectKey, string tenantId, string recipient, int windowSeconds)
    {
        return $"{KeyPrefix}:recipient:{Normalize(projectKey)}:{Normalize(tenantId)}:{Hash(recipient)}:{windowSeconds}";
    }

    private static string Normalize(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())));
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
    }
}
