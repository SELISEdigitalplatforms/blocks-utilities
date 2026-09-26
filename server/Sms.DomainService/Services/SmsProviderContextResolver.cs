using Blocks.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;

namespace Sms.DomainService.Services;

public interface ISmsProviderContextResolver
{
    /// <summary>
    /// Reads the configuration's key from Blocks Secrets. Needs the tenant on the ambient context:
    /// the caller's own in the Api, <c>SmsTenantContext.Enter</c> in the worker and webhooks.
    /// </summary>
    Task<SmsProviderContext> ResolveAsync(string tenantId, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default);
}

public sealed class SmsProviderContextResolver : ISmsProviderContextResolver
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly ISecretService _secrets;
    private readonly IMemoryCache _cache;

    public SmsProviderContextResolver(ISecretService secrets, IMemoryCache cache)
    {
        _secrets = secrets;
        _cache = cache;
    }

    public async Task<SmsProviderContext> ResolveAsync(string tenantId, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.ApiKeySecretId))
        {
            throw new InvalidOperationException($"SMS provider configuration '{configuration.ItemId}' has no API key secret.");
        }

        // The configuration's LastUpdatedDate is part of the key: the only way to rotate a service
        // secret is the save endpoint, which bumps it, and every caller loads the configuration
        // fresh. So a rotation is a miss in every process at once; the expiry only bounds how long
        // a key sits in memory. The tenant is in the key so a hit never skips the read's tenant
        // check for a caller of another tenant.
        var cacheKey = $"sms:api-key:{tenantId}:{configuration.ApiKeySecretId}:{configuration.LastUpdatedDate.Ticks}";
        if (!_cache.TryGetValue(cacheKey, out string? apiKey))
        {
            apiKey = await _secrets.GetValueAsync(configuration.ApiKeySecretId, cancellationToken);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException($"SMS provider API key secret '{configuration.ApiKeySecretId}' is empty.");
            }

            // Only a successful read is cached; a failure is retried on the next call.
            _cache.Set(cacheKey, apiKey, CacheDuration);
        }

        return new SmsProviderContext(tenantId, configuration, apiKey!);
    }
}
