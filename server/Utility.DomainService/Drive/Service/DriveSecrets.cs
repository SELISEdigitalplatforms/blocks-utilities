using System.Collections.Concurrent;
using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Utility.DomainService.Drive.Service;

public interface IDriveSecrets
{
    /// <summary>The Entra client secret for the calling project, or null when not provisioned.</summary>
    Task<string?> GetClientSecretAsync(CancellationToken cancellationToken);

    /// <summary>The AES-GCM key (base64) for the calling project, or null when not provisioned.</summary>
    Task<string?> GetTokenEncryptionKeyAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the drive integration's two secret values from Blocks Secrets, the same way the
/// geolocation provider key and the signature app's IAM client secret are read.
/// </summary>
/// <remarks>
/// Configuration names the secret by id (<see cref="DriveOptions.ClientSecretSecretId"/>,
/// <see cref="DriveOptions.TokenEncryptionKeySecretId"/>); the value lives in blocks-os, per
/// project, rotatable from the console, and every read is audited there. <c>ISecretService</c>
/// is scoped and reads the ambient <see cref="BlocksContext"/>, so a scope is created per read and
/// the result is cached per tenant for <see cref="DriveOptions.SecretCacheSeconds"/>: a rotation
/// is picked up within that window, and a burst of token refreshes does not become a burst of
/// audit rows.
/// </remarks>
public sealed class DriveSecrets : IDriveSecrets
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DriveOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DriveSecrets> _logger;
    private readonly ConcurrentDictionary<string, (string? Value, DateTimeOffset ExpiresAt)> _cache = new(StringComparer.Ordinal);

    public DriveSecrets(
        IServiceScopeFactory scopeFactory,
        IOptions<DriveOptions> options,
        TimeProvider timeProvider,
        ILogger<DriveSecrets> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<string?> GetClientSecretAsync(CancellationToken cancellationToken) =>
        ReadAsync(_options.Value.ClientSecretSecretId, "client secret", cancellationToken);

    public Task<string?> GetTokenEncryptionKeyAsync(CancellationToken cancellationToken) =>
        ReadAsync(_options.Value.TokenEncryptionKeySecretId, "token encryption key", cancellationToken);

    private async Task<string?> ReadAsync(string? secretId, string what, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secretId))
        {
            return null;
        }

        var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
        var key = $"{tenantId}:{secretId}";
        var now = _timeProvider.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Value;
        }

        string? value = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
            value = await secrets.GetValueAsync(secretId, cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
                _logger.LogWarning("Drive {What} is empty in Blocks Secrets SecretId={SecretId}", what, secretId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Not found, access denied, locked, vault unreachable, or no tenant in context.
            _logger.LogError(exception, "Reading the drive {What} from Blocks Secrets failed SecretId={SecretId}", what, secretId);
        }

        // Cached either way so an unprovisioned project does not pay a failed round trip per call.
        _cache[key] = (value, now.AddSeconds(_options.Value.SecretCacheSeconds));
        return value;
    }
}
