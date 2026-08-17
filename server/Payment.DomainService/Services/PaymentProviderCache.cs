using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentProviderCache : IPaymentProviderCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly Dictionary<string, DateTime>
        _refreshAttempts = new();
    private readonly object _refreshGate = new();
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly IPaymentProviderSecretHydrator _secrets;

    public PaymentProviderCache(
        IOptionsMonitor<PaymentOptions> options,
        IPaymentProviderSecretHydrator secrets)
    {
        _options = options;
        _secrets = secrets;
    }

    public async Task<PaymentProvider?> GetAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        Func<Task<PaymentProvider?>> loader)
    {
        var key = CreateKey(tenantId, organizationId, providerName);

        if (_entries.TryGetValue(key, out var existing) &&
            existing.ExpiresAtUtc > DateTime.UtcNow)
        {
            return existing.Provider;
        }

        var provider = await loader();

        if (provider == null ||
            !await _secrets.HydrateAsync(
                provider,
                CancellationToken.None))
        {
            return null;
        }

        if (_entries.Count >= 1000)
        {
            RemoveExpired();
        }

        _entries[key] = new Entry(
            provider,
            DateTime.UtcNow.AddSeconds(
                Math.Clamp(
                    _options.CurrentValue.ProviderCacheSeconds,
                    10,
                    300)));

        return provider;
    }

    public async Task<PaymentProvider?> RefreshAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        Func<Task<PaymentProvider?>> loader)
    {
        var key = CreateKey(tenantId, organizationId, providerName);

        if (!ShouldRefresh(key))
        {
            return await GetAsync(
                tenantId,
                organizationId,
                providerName,
                loader);
        }

        _entries.TryGetValue(key, out var existing);
        var provider = await loader();

        if (provider == null ||
            !await _secrets.HydrateAsync(
                provider,
                CancellationToken.None))
        {
            return existing?.Provider;
        }

        _entries[key] = new Entry(
            provider,
            DateTime.UtcNow.AddSeconds(
                Math.Clamp(
                    _options.CurrentValue.ProviderCacheSeconds,
                    10,
                    300)));

        return provider;
    }

    public void Remove(
        string tenantId,
        string? organizationId,
        string providerName) =>
        _entries.TryRemove(
            CreateKey(tenantId, organizationId, providerName),
            out _);

    public void RemoveAll(string tenantId, string providerName)
    {
        var prefix = $"{tenantId}:";
        var suffix = $":{providerName}";

        // Keys is a snapshot on a concurrent dictionary, so removing while walking it is safe.
        foreach (var key in _entries.Keys)
        {
            // The provider is matched without regard to case, unlike the key that was written.
            // Resolution matches provider names case-insensitively, so two spellings can each
            // hold an entry for the same configuration; over-evicting costs one re-read, while
            // under-evicting leaves decrypted credentials in memory after they were replaced.
            if (key.StartsWith(prefix, StringComparison.Ordinal) &&
                key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Separates an organization's entry from the tenant-level one it may fall back to, so a
    /// second organization is never served the first one's configuration.
    /// </summary>
    private static string CreateKey(
        string tenantId,
        string? organizationId,
        string providerName) =>
        $"{tenantId}:{(string.IsNullOrWhiteSpace(organizationId) ? "-" : organizationId)}:{providerName}";

    private bool ShouldRefresh(string key)
    {
        var now = DateTime.UtcNow;
        var throttle = TimeSpan.FromSeconds(
            Math.Clamp(
                _options.CurrentValue
                    .ProviderSecretRefreshThrottleSeconds,
                5,
                300));

        lock (_refreshGate)
        {
            if (_refreshAttempts.TryGetValue(
                    key,
                    out var lastAttempt) &&
                now - lastAttempt < throttle)
            {
                return false;
            }

            _refreshAttempts[key] = now;

            if (_refreshAttempts.Count >= 1000)
            {
                foreach (var expired in _refreshAttempts
                             .Where(item =>
                                 now - item.Value >= throttle)
                             .Take(250)
                             .Select(item => item.Key)
                             .ToArray())
                {
                    _refreshAttempts.Remove(expired);
                }
            }

            return true;
        }
    }

    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;

        foreach (var item in _entries
                     .Where(entry =>
                         entry.Value.ExpiresAtUtc <= now)
                     .Take(250))
        {
            _entries.TryRemove(item.Key, out _);
        }

        if (_entries.Count >= 1000)
        {
            _entries.Clear();
        }
    }

    private sealed record Entry(
        PaymentProvider Provider,
        DateTime ExpiresAtUtc);
}
