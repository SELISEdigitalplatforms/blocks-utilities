using Blocks.Secrets;
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
    private readonly ISecretService _secrets;

    public SmsProviderContextResolver(ISecretService secrets)
    {
        _secrets = secrets;
    }

    public async Task<SmsProviderContext> ResolveAsync(string tenantId, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.ApiKeySecretId))
        {
            throw new InvalidOperationException($"SMS provider configuration '{configuration.ItemId}' has no API key secret.");
        }

        var apiKey = await _secrets.GetValueAsync(configuration.ApiKeySecretId, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"SMS provider API key secret '{configuration.ApiKeySecretId}' is empty.");
        }

        return new SmsProviderContext(tenantId, configuration, apiKey);
    }
}
