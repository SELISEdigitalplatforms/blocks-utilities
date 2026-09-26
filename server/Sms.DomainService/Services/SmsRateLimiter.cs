using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Sms.DomainService.Entities;
using StackExchange.Redis;

namespace Sms.DomainService.Services;

/// <summary>
/// Fixed-window counters in Redis, with separate limits for the tenant and for each recipient,
/// both from the provider configuration. Fails closed: no Redis, no SMS.
/// </summary>
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

    public async Task<SmsRateLimitResult> CheckAsync(string tenantId, IReadOnlyCollection<string> destinationNumbers, SmsRateLimitSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var cache = _cacheClient.CacheDatabase();
            var recipients = destinationNumbers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            // Recipients first, so a request refused for one flooded number does not also spend the
            // tenant's allowance. ponytail: recipient counters for numbers checked before the refused
            // one are still spent; a Lua script can make the whole check atomic if that matters.
            foreach (var recipient in recipients)
            {
                var key = $"{KeyPrefix}:recipient:{Hash(tenantId)}:{Hash(recipient)}:{settings.RecipientWindowSeconds}";
                if (await ConsumeAsync(cache, key, 1, settings.RecipientWindowSeconds) > settings.RecipientMaxPerWindow)
                {
                    _logger.LogWarning("SMS recipient rate limit exceeded TenantId={TenantId}, RecipientHash={RecipientHash}, Max={Max}, WindowSeconds={WindowSeconds}",
                        tenantId, Hash(recipient), settings.RecipientMaxPerWindow, settings.RecipientWindowSeconds);
                    return SmsRateLimitResult.Blocked("Recipient SMS rate limit exceeded.");
                }
            }

            // The tenant limit counts SMS, one per recipient, not API calls.
            var tenantKey = $"{KeyPrefix}:tenant:{Hash(tenantId)}:{settings.TenantWindowSeconds}";
            if (await ConsumeAsync(cache, tenantKey, recipients.Length, settings.TenantWindowSeconds) > settings.TenantMaxPerWindow)
            {
                _logger.LogWarning("SMS tenant rate limit exceeded TenantId={TenantId}, Max={Max}, WindowSeconds={WindowSeconds}",
                    tenantId, settings.TenantMaxPerWindow, settings.TenantWindowSeconds);
                return SmsRateLimitResult.Blocked("Tenant SMS rate limit exceeded.");
            }

            return SmsRateLimitResult.Allowed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS rate limiter failed closed TenantId={TenantId}", tenantId);
            return SmsRateLimitResult.Blocked("SMS rate limiter is unavailable.");
        }
    }

    private static async Task<long> ConsumeAsync(IDatabase cache, string key, int amount, int windowSeconds)
    {
        var count = await cache.StringIncrementAsync(key, amount).ConfigureAwait(false);
        if (count == amount)
        {
            await cache.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds)).ConfigureAwait(false);
        }

        return count;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())));
}
