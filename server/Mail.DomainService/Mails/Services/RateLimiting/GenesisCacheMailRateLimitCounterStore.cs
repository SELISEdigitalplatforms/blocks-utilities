using System.Collections.Concurrent;
using System.Globalization;
using Blocks.Genesis;

namespace Mail.DomainService.Mails.Services.RateLimiting;

public sealed class GenesisCacheMailRateLimitCounterStore : IMailRateLimitCounterStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new();
    private readonly ICacheClient _cacheClient;

    public GenesisCacheMailRateLimitCounterStore(ICacheClient cacheClient)
    {
        _cacheClient = cacheClient;
    }

    public async Task<MailRateLimitCounterClaimResult> TryClaimAsync(
        MailRateLimitCounterClaim claim,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"blocks:{claim.LimiterKey}:{claim.WindowStartUtc.Ticks}";
        var keyLock = KeyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(cancellationToken);

        try
        {
            var cachedValue = await _cacheClient.GetStringValueAsync(cacheKey);
            var current = long.TryParse(cachedValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? Math.Max(0, parsed)
                : 0;
            var attempted = current + Math.Max(1, claim.Cost);
            var limit = Math.Max(1, claim.Limit);

            if (attempted > limit)
            {
                return CreateResult(false, attempted, limit, claim.WindowEndUtc);
            }

            var ttlSeconds = Math.Max(
                1,
                (long)Math.Ceiling((claim.WindowEndUtc - DateTime.UtcNow).TotalSeconds));
            var saved = await _cacheClient.AddStringValueAsync(
                cacheKey,
                attempted.ToString(CultureInfo.InvariantCulture),
                ttlSeconds);
            if (!saved)
            {
                throw new InvalidOperationException("The Genesis cache client could not persist the rate-limit counter.");
            }

            return CreateResult(true, attempted, limit, claim.WindowEndUtc);
        }
        finally
        {
            keyLock.Release();
            if (keyLock.CurrentCount == 1)
            {
                KeyLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(cacheKey, keyLock));
            }
        }
    }

    private static MailRateLimitCounterClaimResult CreateResult(
        bool isAllowed,
        long used,
        int limit,
        DateTime windowEndUtc)
    {
        return new MailRateLimitCounterClaimResult
        {
            IsAllowed = isAllowed,
            Used = (int)Math.Min(int.MaxValue, used),
            Limit = limit,
            WindowEndUtc = windowEndUtc
        };
    }
}
