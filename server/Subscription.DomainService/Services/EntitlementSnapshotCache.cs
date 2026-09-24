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

    public async Task<IReadOnlyList<SubscriptionDetail>> GetAsync(
        string tenantId,
        string organizationId,
        string subscriberUserId,
        Func<Task<IReadOnlyList<SubscriptionDetail>>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        var key = CreateKey(tenantId, organizationId, subscriberUserId);
        var now = _time.GetUtcNow().UtcDateTime;

        if (_entries.TryGetValue(key, out var cached) && cached.ExpiresAtUtc > now)
        {
            return cached.Subscriptions;
        }

        var subscriptions = await loader();

        if (_entries.Count >= MaximumEntries)
        {
            RemoveExpired(now);
        }

        _entries[key] = new Entry(
            subscriptions,
            now.AddSeconds(Math.Max(0, _options.CurrentValue.EntitlementCacheSeconds)));

        return subscriptions;
    }

    public void Invalidate(string tenantId, string organizationId)
    {
        var prefix = OrganizationPrefix(tenantId, organizationId);

        // A scan rather than a single removal, because one organization now holds an entry per
        // subscriber and each of them carries the organization's own subscription. The dictionary
        // is capped at MaximumEntries, so this is bounded and runs only when a subscription
        // actually changes — not on the read path.
        foreach (var pair in _entries)
        {
            if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

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

    private static string OrganizationPrefix(string tenantId, string organizationId) =>
        $"{tenantId}:{organizationId}:";

    /// <summary>
    /// The subscriber's own slot, under a prefix <see cref="Invalidate"/> can sweep.
    /// </summary>
    /// <remarks>
    /// The organization-wide subscriber is the empty string, so its key ends in the separator and
    /// cannot be confused with any user's — and no identifier here contains one, which is what
    /// makes the prefix unambiguous.
    /// </remarks>
    private static string CreateKey(
        string tenantId,
        string organizationId,
        string subscriberUserId) =>
        OrganizationPrefix(tenantId, organizationId) + subscriberUserId;

    private sealed record Entry(
        IReadOnlyList<SubscriptionDetail> Subscriptions,
        DateTime ExpiresAtUtc);
}
