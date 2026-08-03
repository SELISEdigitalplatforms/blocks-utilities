using System.Collections.Concurrent;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Reads each scope's key ring from the vault on demand and caches it.
/// </summary>
/// <remarks>
/// Three properties are what make this safe, and each fails quietly if dropped.
/// <list type="bullet">
/// <item>Entries expire. Without a lifetime a rotated ring is never picked up by a running
/// process, so rotation appears to work and silently does not.</item>
/// <item>An evicted ring is disposed, because <c>Dispose</c> is what zeroes its key material —
/// but only after a grace period, since a caller may still be mid-operation with it.</item>
/// <item>A failed read is cached briefly too, so a missing secret does not turn every payment
/// into a vault round trip, while a newly provisioned ring still appears within a minute.</item>
/// </list>
/// </remarks>
public sealed class ProviderTokenEncryptionKeyRingProvider :
    IProviderTokenEncryptionKeyRingProvider,
    IDisposable
{
    private static readonly IProviderTokenEncryptionKeyRing Unavailable =
        new UnavailableProviderTokenEncryptionKeyRing();

    private readonly ConcurrentDictionary<string, CacheEntry> _entries =
        new(StringComparer.Ordinal);

    private readonly IVault _vault;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<ProviderTokenEncryptionKeyRingProvider> _logger;
    private readonly TimeProvider _time;
    private bool _disposed;

    public ProviderTokenEncryptionKeyRingProvider(
        IVault vault,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<ProviderTokenEncryptionKeyRingProvider> logger,
        TimeProvider? time = null)
    {
        _vault = vault;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async ValueTask<IProviderTokenEncryptionKeyRing> GetAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveAsync(scope, cancellationToken);

        return resolution.KeyRing;
    }

    public async ValueTask<PaymentKeyRingHealth> CheckAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken = default)
    {
        var secretName = PaymentKeyRingSecretName.Create(scope);

        // Deliberately not cached: the diagnostic exists to answer "is the ring I just
        // provisioned readable", which a cached failure would answer wrongly for a full minute.
        var resolution = await LoadAsync(scope, cancellationToken);

        Evict(secretName);

        return new PaymentKeyRingHealth(
            resolution.IsReadable,
            secretName,
            resolution.UsedSharedKeyRing,
            resolution.KeyRing.ActiveKeyId,
            resolution.FailureReason);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        foreach (var entry in _entries.Values)
        {
            if (entry.Loader.IsValueCreated &&
                entry.Loader.Value.IsCompletedSuccessfully)
            {
                entry.Loader.Value.Result.KeyRing.Dispose();
            }
        }

        _entries.Clear();
    }

    private async ValueTask<KeyRingResolution> ResolveAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var secretName = PaymentKeyRingSecretName.Create(scope);
        var now = _time.GetUtcNow();

        while (true)
        {
            var entry = _entries.GetOrAdd(
                secretName,
                _ => new CacheEntry(
                    new Lazy<Task<KeyRingResolution>>(
                        () => LoadAsync(scope, CancellationToken.None)
                            .AsTask()),
                    now));

            var resolution = await entry.Loader.Value.WaitAsync(
                cancellationToken);

            if (!IsExpired(entry, resolution, now))
            {
                return resolution;
            }

            // Expired. Only the thread that succeeds in removing this exact entry retires the
            // ring, so concurrent callers do not each queue a disposal of the same one.
            if (Evict(secretName, entry))
            {
                Retire(resolution.KeyRing);
            }
        }
    }

    private bool IsExpired(
        CacheEntry entry,
        KeyRingResolution resolution,
        DateTimeOffset now)
    {
        var options = _options.CurrentValue;
        var lifetime = resolution.IsReadable
            ? Math.Max(options.EncryptionKeyRingCacheSeconds, 1)
            : Math.Max(options.EncryptionKeyRingFailureCacheSeconds, 1);

        return now - entry.LoadedAtUtc >= TimeSpan.FromSeconds(lifetime);
    }

    private bool Evict(
        string secretName,
        CacheEntry? expected = null) =>
        expected == null
            ? _entries.TryRemove(secretName, out _)
            : ((System.Collections.Generic.ICollection<
                    KeyValuePair<string, CacheEntry>>)_entries)
                .Remove(new KeyValuePair<string, CacheEntry>(
                    secretName,
                    expected));

    /// <summary>
    /// Disposes a ring after a grace period. Disposal zeroes the key bytes, and a caller that
    /// fetched the ring a moment ago may still be encrypting with it — zeroing underneath it
    /// would surface as an authentication failure, which reads like data corruption.
    /// </summary>
    private void Retire(IProviderTokenEncryptionKeyRing keyRing)
    {
        var grace = TimeSpan.FromSeconds(
            Math.Max(_options.CurrentValue.EncryptionKeyRingDisposalGraceSeconds, 1));

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(grace, _time);
                    keyRing.Dispose();
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to retire an evicted payment encryption key ring.");
                }
            });
    }

    private async ValueTask<KeyRingResolution> LoadAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken)
    {
        var secretName = PaymentKeyRingSecretName.Create(scope);

        try
        {
            var keyRing = await ProviderTokenEncryptionKeyRingVaultLoader
                .LoadAsync(_vault, secretName);

            return new KeyRingResolution(
                keyRing,
                IsReadable: true,
                UsedSharedKeyRing: false,
                FailureReason: string.Empty);
        }
        catch (Exception exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await FallBackAsync(scope, secretName, exception);
        }
    }

    private async ValueTask<KeyRingResolution> FallBackAsync(
        PaymentEncryptionScope scope,
        string secretName,
        Exception exception)
    {
        if (!_options.CurrentValue.FallBackToSharedEncryptionKeyRing)
        {
            _logger.LogError(
                exception,
                "The payment encryption key ring {SecretName} for {Scope} is unavailable; payments for it will fail closed.",
                secretName,
                scope.ToString());

            return new KeyRingResolution(
                Unavailable,
                IsReadable: false,
                UsedSharedKeyRing: false,
                FailureReason: "key_ring_unavailable");
        }

        try
        {
            var shared = await ProviderTokenEncryptionKeyRingVaultLoader
                .LoadAsync(_vault, PaymentKeyRingSecretName.SharedSecretName);

            _logger.LogWarning(
                "{Scope} has no key ring of its own; falling back to the shared ring. Provision {SecretName} and re-encrypt to isolate it.",
                scope.ToString(),
                secretName);

            return new KeyRingResolution(
                shared,
                IsReadable: true,
                UsedSharedKeyRing: true,
                FailureReason: string.Empty);
        }
        catch (Exception sharedException)
        {
            _logger.LogError(
                sharedException,
                "Neither {SecretName} nor the shared payment encryption key ring could be read for {Scope}.",
                secretName,
                scope.ToString());

            return new KeyRingResolution(
                Unavailable,
                IsReadable: false,
                UsedSharedKeyRing: false,
                FailureReason: "key_ring_unavailable");
        }
    }

    private sealed record CacheEntry(
        Lazy<Task<KeyRingResolution>> Loader,
        DateTimeOffset LoadedAtUtc);

    private sealed record KeyRingResolution(
        IProviderTokenEncryptionKeyRing KeyRing,
        bool IsReadable,
        bool UsedSharedKeyRing,
        string FailureReason);
}
