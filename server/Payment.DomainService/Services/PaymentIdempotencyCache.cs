using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public sealed class PaymentIdempotencyCache : IPaymentIdempotencyCache
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(1);
    private readonly ICacheClient _cacheClient;
    private readonly ILogger<PaymentIdempotencyCache> _logger;

    public PaymentIdempotencyCache(ICacheClient cacheClient, ILogger<PaymentIdempotencyCache> logger)
    {
        _cacheClient = cacheClient;
        _logger = logger;
    }

    public async Task<string?> GetPaymentIdAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var value = await _cacheClient.CacheDatabase().StringGetAsync(Key(tenantId, idempotencyKey));
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Payment idempotency cache read unavailable ExceptionType={ExceptionType}",
                ex.GetType().Name);
            return null;
        }
    }

    public async Task SetPaymentIdAsync(
        string tenantId,
        string idempotencyKey,
        string paymentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _cacheClient.CacheDatabase().StringSetAsync(Key(tenantId, idempotencyKey), paymentId, TimeToLive);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Payment idempotency cache write unavailable PaymentId={PaymentId} ExceptionType={ExceptionType}",
                paymentId,
                ex.GetType().Name);
        }
    }

    private static RedisKey Key(string tenantId, string idempotencyKey) =>
        $"payment:idempotency:{Hash(tenantId)}:{Hash(idempotencyKey)}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())))[..24];
}
