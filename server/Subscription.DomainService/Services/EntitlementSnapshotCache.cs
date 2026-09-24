using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Holds an organization's subscription briefly, so the hot path is not a database read per
/// gated action.
/// </summary>
/// <remarks>
/// Only the subscription is cached, and only for seconds. Usage counters never are: they are
/// the volatile half, and a stale one would let a caller past an allowance that is already
/// spent. The subscription changes rarely and its staleness is bounded and deliberate.
/// </remarks>
public sealed class EntitlementSnapshotCache : IEntitlementSnapshotCache
{
    private const int MaximumEntries = 5_000;

    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(StringComparer.Ordinal);

    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly TimeProvider _time;

    public EntitlementSnapshotCache(
        IOptionsMonitor<SubscriptionOptions> options,
        TimeProvider? time = null)
    {
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionDetail?> GetAsync(
        string tenantId,
        string organizationId,
        Func<Task<SubscriptionDetail?>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        var key = CreateKey(tenantId, organizationId);
        var now = _time.GetUtcNow().UtcDateTime;

        if (_entries.TryGetValue(key, out var cached) && cached.ExpiresAtUtc > now)
        {
            return cached.Subscription;
        }

        var subscription = await loader();

        if (_entries.Count >= MaximumEntries)
        {
            RemoveExpired(now);
        }

        _entries[key] = new Entry(
            subscription,
            now.AddSeconds(Math.Max(0, _options.CurrentValue.EntitlementCacheSeconds)));

        return subscription;
    }

    public void Invalidate(string tenantId, string organizationId) =>
        _entries.TryRemove(CreateKey(tenantId, organizationId), out _);

    private void RemoveExpired(DateTime now)
    {
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string CreateKey(string tenantId, string organizationId) =>
        $"{tenantId}:{organizationId}";

    private sealed record Entry(SubscriptionDetail? Subscription, DateTime ExpiresAtUtc);
}
