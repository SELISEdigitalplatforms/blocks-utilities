using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public sealed class StoredPaymentMethodRateLimiter :
    IStoredPaymentMethodRateLimiter
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
    private readonly ILogger<StoredPaymentMethodRateLimiter> _logger;

    public StoredPaymentMethodRateLimiter(
        ICacheClient cacheClient,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StoredPaymentMethodRateLimiter> logger)
    {
        _cacheClient = cacheClient;
        _options = options;
        _logger = logger;
    }

    public Task<PaymentRateLimitResult> CheckListAsync(
        string tenantId,
        string actorId,
        CancellationToken cancellationToken) =>
        CheckAsync(
            tenantId,
            actorId,
            "list",
            Math.Max(
                1,
                _options.CurrentValue
                    .StoredPaymentMethodListRequestsPerMinute),
            cancellationToken);

    public Task<PaymentRateLimitResult> CheckRemovalAsync(
        string tenantId,
        string actorId,
        CancellationToken cancellationToken) =>
        CheckAsync(
            tenantId,
            actorId,
            "remove",
            Math.Max(
                1,
                _options.CurrentValue
                    .StoredPaymentMethodRemovalRequestsPerMinute),
            cancellationToken);

    private async Task<PaymentRateLimitResult> CheckAsync(
        string tenantId,
        string actorId,
        string operation,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var key =
                $"payment:rate:method:{operation}:{Hash(tenantId)}:{Hash(actorId)}";
            var database = _cacheClient.CacheDatabase();
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
                    "Invalid stored payment method rate limiter response.");
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
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Stored payment method rate limiter unavailable TenantHash={TenantHash} Operation={Operation}",
                Hash(tenantId),
                operation);

            return new PaymentRateLimitResult
            {
                IsAvailable = false,
                IsAllowed = false,
                RetryAfterSeconds = 30
            };
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    value.Trim().ToLowerInvariant())))[..24];
}
