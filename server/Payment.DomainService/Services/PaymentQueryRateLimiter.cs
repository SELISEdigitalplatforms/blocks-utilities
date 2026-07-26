using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public sealed class PaymentQueryRateLimiter :
    IPaymentQueryRateLimiter
{
    private const string TokenBucketScript = """
local capacity = tonumber(ARGV[1])
local refillPerMs = tonumber(ARGV[2])
local now = tonumber(ARGV[3])
local ttl = tonumber(ARGV[4])
local data = redis.call('HMGET', KEYS[1], 'tokens', 'timestamp')
local tokens = tonumber(data[1]) or capacity
local timestamp = tonumber(data[2]) or now
tokens = math.min(capacity, tokens + math.max(0, now - timestamp) * refillPerMs)
local allowed = 0
local retryMs = 0
if tokens >= 1 then
  tokens = tokens - 1
  allowed = 1
else
  retryMs = math.ceil((1 - tokens) / refillPerMs)
end
redis.call('HSET', KEYS[1], 'tokens', tokens, 'timestamp', now)
redis.call('PEXPIRE', KEYS[1], ttl)
return {allowed, math.floor(tokens), retryMs}
""";

    private readonly ICacheClient _cacheClient;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentQueryRateLimiter> _logger;

    public PaymentQueryRateLimiter(
        ICacheClient cacheClient,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentQueryRateLimiter> logger)
    {
        _cacheClient = cacheClient;
        _options = options;
        _logger = logger;
    }

    public async Task<PaymentRateLimitResult> CheckAsync(
        string tenantId,
        string actorId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var database = _cacheClient.CacheDatabase();
            var tenantResult = await ConsumeAsync(
                database,
                $"payment:rate:query:tenant:{Hash(tenantId)}",
                Math.Max(
                    1,
                    _options.CurrentValue
                        .PaymentQueryTenantRequestsPerMinute));

            if (!tenantResult.IsAllowed)
            {
                return tenantResult;
            }

            var actorResult = await ConsumeAsync(
                database,
                $"payment:rate:query:actor:{Hash(tenantId)}:{Hash(actorId)}",
                Math.Max(
                    1,
                    _options.CurrentValue
                        .PaymentQueryActorRequestsPerMinute));

            return actorResult.IsMoreRestrictiveThan(tenantResult)
                ? actorResult
                : tenantResult;
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Payment query rate limiter unavailable TenantHash={TenantHash}",
                Hash(tenantId));

            return new PaymentRateLimitResult
            {
                IsAvailable = false,
                IsAllowed = false,
                RetryAfterSeconds = 30
            };
        }
    }

    private static async Task<PaymentRateLimitResult> ConsumeAsync(
        IDatabase database,
        string key,
        int limit)
    {
        const int windowMilliseconds = 60_000;
        var result = (RedisResult[]?)await database.ScriptEvaluateAsync(
            TokenBucketScript,
            [new RedisKey(key)],
            [
                limit,
                (double)limit / windowMilliseconds,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                windowMilliseconds * 2
            ]);

        if (result is not { Length: 3 })
        {
            throw new InvalidOperationException(
                "Invalid payment query rate limiter response.");
        }

        var allowed = (long)result[0] == 1;
        var remaining = Math.Max(0, (int)(long)result[1]);
        var retryMilliseconds = (long)result[2];

        return new PaymentRateLimitResult
        {
            IsAllowed = allowed,
            Limit = limit,
            Remaining = remaining,
            RetryAfterSeconds = allowed
                ? 0
                : Math.Max(
                    1,
                    (int)Math.Ceiling(
                        retryMilliseconds / 1000d)),
            ResetAfterSeconds = Math.Max(
                1,
                (int)Math.Ceiling(
                    (limit - remaining) * 60d / limit))
        };
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    value.Trim().ToLowerInvariant())))[..24];
}
