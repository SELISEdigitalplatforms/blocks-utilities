using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public sealed class CheckoutCallbackRateLimiter : ICheckoutCallbackRateLimiter
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
if tokens >= 1 then tokens = tokens - 1 allowed = 1 else retryMs = math.ceil((1 - tokens) / refillPerMs) end
redis.call('HSET', KEYS[1], 'tokens', tokens, 'timestamp', now)
redis.call('PEXPIRE', KEYS[1], ttl)
return {allowed, math.floor(tokens), retryMs}
""";

    private readonly ICacheClient _cache;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<CheckoutCallbackRateLimiter> _logger;
    public CheckoutCallbackRateLimiter(ICacheClient cache, IOptionsMonitor<PaymentOptions> options, ILogger<CheckoutCallbackRateLimiter> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<PaymentRateLimitResult> CheckAsync(string clientAddress, string signedState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = _options.CurrentValue;
        var rules = new[]
        {
            ($"payment:rate:return:client:{Hash(clientAddress)}", Math.Max(1, options.ReturnRequestsPerClientPerMinute)),
            ($"payment:rate:return:state:{Hash(signedState)}", Math.Max(1, options.ReturnRequestsPerStatePerMinute))
        };
        try
        {
            var database = _cache.CacheDatabase();
            PaymentRateLimitResult? tightest = null;
            foreach (var rule in rules)
            {
                var result = await ConsumeAsync(database, rule.Item1, rule.Item2);
                tightest = tightest == null || result.IsMoreRestrictiveThan(tightest) ? result : tightest;
                if (!result.IsAllowed) return result;
            }
            return tightest!;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Checkout callback rate limiter unavailable ExceptionType={ExceptionType}", ex.GetType().Name);
            return new PaymentRateLimitResult { IsAvailable = false, IsAllowed = false, RetryAfterSeconds = 30 };
        }
    }

    private static async Task<PaymentRateLimitResult> ConsumeAsync(IDatabase database, string key, int limit)
    {
        const int windowMs = 60_000;
        var result = (RedisResult[]?)await database.ScriptEvaluateAsync(TokenBucketScript, [new RedisKey(key)],
            [limit, (double)limit / windowMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), windowMs * 2]);
        if (result is not { Length: 3 }) throw new InvalidOperationException("Invalid return rate limiter response.");
        var allowed = (long)result[0] == 1;
        var remaining = (int)(long)result[1];
        var retryMs = (long)result[2];
        return new PaymentRateLimitResult
        {
            IsAllowed = allowed,
            Limit = limit,
            Remaining = Math.Max(0, remaining),
            RetryAfterSeconds = allowed ? 0 : Math.Max(1, (int)Math.Ceiling(retryMs / 1000d)),
            ResetAfterSeconds = Math.Max(1, (int)Math.Ceiling((limit - remaining) * 60d / limit))
        };
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}
