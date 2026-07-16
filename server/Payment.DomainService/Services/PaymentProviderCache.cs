using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentProviderCache : IPaymentProviderCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly IOptionsMonitor<PaymentOptions> _options;
    public PaymentProviderCache(IOptionsMonitor<PaymentOptions> options) => _options = options;

    public async Task<PaymentProvider?> GetAsync(string tenantId, string providerName, Func<Task<PaymentProvider?>> loader)
    {
        var key = $"{tenantId}:{providerName}";
        if (_entries.TryGetValue(key, out var existing) && existing.ExpiresAtUtc > DateTime.UtcNow) return existing.Provider;
        var provider = await loader();
        if (provider != null)
        {
            if (_entries.Count >= 1000) RemoveExpired();
            _entries[key] = new Entry(provider, DateTime.UtcNow.AddSeconds(Math.Max(10, _options.CurrentValue.ProviderCacheSeconds)));
        }
        return provider;
    }

    public void Remove(string tenantId, string providerName) => _entries.TryRemove($"{tenantId}:{providerName}", out _);
    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var item in _entries.Where(x => x.Value.ExpiresAtUtc <= now).Take(250)) _entries.TryRemove(item.Key, out _);
        if (_entries.Count >= 1000) _entries.Clear();
    }
    private sealed record Entry(PaymentProvider Provider, DateTime ExpiresAtUtc);
}
