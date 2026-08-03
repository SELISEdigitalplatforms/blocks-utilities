using System.Diagnostics;
using System.Security.Cryptography;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public sealed class PaymentDistributedLock : IPaymentDistributedLock
{
    private readonly ICacheClient _cacheClient;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly IPaymentLockRenewalScheduler _renewalScheduler;
    private readonly ILogger<PaymentDistributedLock> _logger;

    public PaymentDistributedLock(
        ICacheClient cacheClient,
        IOptionsMonitor<PaymentOptions> options,
        IPaymentLockRenewalScheduler renewalScheduler,
        ILogger<PaymentDistributedLock> logger)
    {
        _cacheClient = cacheClient;
        _options = options;
        _renewalScheduler = renewalScheduler;
        _logger = logger;
    }

    public async Task<IPaymentLockHandle?> TryAcquireAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var database = _cacheClient.CacheDatabase();
            var key = new RedisKey($"payment:lock:{resource}");
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var lease = TimeSpan.FromSeconds(
                Math.Clamp(_options.CurrentValue.DistributedLockSeconds, 5, 60));
            var wait = TimeSpan.FromMilliseconds(
                Math.Clamp(_options.CurrentValue.DistributedLockWaitMilliseconds, 0, 5_000));
            var stopwatch = Stopwatch.StartNew();

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await database.StringSetAsync(key, token, lease, When.NotExists))
                {
                    return new Handle(
                        database,
                        key,
                        token,
                        lease,
                        _renewalScheduler,
                        _logger);
                }

                var remaining = wait - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero) return null;

                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Min(75, remaining.TotalMilliseconds)),
                    cancellationToken);
            } while (true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Payment distributed lock unavailable; MongoDB will arbitrate Resource={Resource} ExceptionType={ExceptionType}",
                resource,
                ex.GetType().Name);
            return null;
        }
    }

    private sealed class Handle : IPaymentLockHandle
    {
        private const string ReleaseScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
        private const string RenewScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";

        private readonly IDatabase _database;
        private readonly RedisKey _key;
        private readonly TimeSpan _lease;
        private readonly IPaymentLockRenewalScheduler _renewalScheduler;
        private readonly ILogger<PaymentDistributedLock> _logger;
        private readonly CancellationTokenSource _renewalCancellation = new();
        private readonly Task _renewalTask;
        private int _disposed;

        public Handle(
            IDatabase database,
            RedisKey key,
            string token,
            TimeSpan lease,
            IPaymentLockRenewalScheduler renewalScheduler,
            ILogger<PaymentDistributedLock> logger)
        {
            _database = database;
            _key = key;
            Token = token;
            _lease = lease;
            _renewalScheduler = renewalScheduler;
            _logger = logger;
            _renewalTask = RenewUntilDisposedAsync();
        }

        public string Token { get; }

        public async Task<bool> RenewAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) != 0) return false;

            var result = await _database.ScriptEvaluateAsync(
                RenewScript,
                [_key],
                [Token, (long)_lease.TotalMilliseconds]);
            return (long)result == 1;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            await _renewalCancellation.CancelAsync();
            try
            {
                await _renewalTask;
            }
            catch (OperationCanceledException) when (_renewalCancellation.IsCancellationRequested)
            {
                // Expected while stopping the renewal loop.
            }

            try
            {
                await _database.ScriptEvaluateAsync(ReleaseScript, [_key], [Token]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Payment distributed lock release failed LockKey={LockKey} ExceptionType={ExceptionType}",
                    _key,
                    ex.GetType().Name);
            }
            finally
            {
                _renewalCancellation.Dispose();
            }
        }

        private async Task RenewUntilDisposedAsync()
        {
            try
            {
                while (true)
                {
                    await _renewalScheduler.WaitForRenewalAsync(
                        _lease,
                        _renewalCancellation.Token);
                    var renewed = await RenewAsync(_renewalCancellation.Token);
                    if (renewed) continue;

                    if (!_renewalCancellation.IsCancellationRequested)
                    {
                        _logger.LogWarning(
                            "Payment distributed lock ownership was lost LockKey={LockKey}",
                            _key);
                    }
                    return;
                }
            }
            catch (OperationCanceledException) when (_renewalCancellation.IsCancellationRequested)
            {
                // Expected when the critical section completes.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Payment distributed lock renewal stopped LockKey={LockKey} ExceptionType={ExceptionType}",
                    _key,
                    ex.GetType().Name);
            }
        }
    }
}
