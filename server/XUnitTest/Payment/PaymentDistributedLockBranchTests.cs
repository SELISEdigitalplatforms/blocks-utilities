using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace XUnitTest.Payment;

public sealed class PaymentDistributedLockBranchTests
{
    private static IOptionsMonitor<PaymentOptions> Options(PaymentOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        return monitor.Object;
    }

    [Fact]
    public async Task Returns_null_when_lock_is_already_held_and_wait_budget_is_zero()
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                When.NotExists))
            .ReturnsAsync(false);
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase()).Returns(database.Object);
        var distributedLock = new PaymentDistributedLock(
            cache.Object,
            Options(new PaymentOptions { DistributedLockWaitMilliseconds = 0 }),
            Mock.Of<IPaymentLockRenewalScheduler>(),
            NullLogger<PaymentDistributedLock>.Instance);

        var handle = await distributedLock.TryAcquireAsync(
            "resource-1", CancellationToken.None);

        handle.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_cache_backend_is_unavailable()
    {
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase())
            .Throws(new InvalidOperationException("redis down"));
        var distributedLock = new PaymentDistributedLock(
            cache.Object,
            Options(new PaymentOptions()),
            Mock.Of<IPaymentLockRenewalScheduler>(),
            NullLogger<PaymentDistributedLock>.Instance);

        var handle = await distributedLock.TryAcquireAsync(
            "resource-1", CancellationToken.None);

        handle.Should().BeNull();
    }

    [Fact]
    public async Task Dispose_swallows_release_script_failures()
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                When.NotExists))
            .ReturnsAsync(true);
        database.Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.SocketFailure, "release failed"));
        var cache = new Mock<ICacheClient>();
        cache.Setup(client => client.CacheDatabase()).Returns(database.Object);
        var scheduler = new BlockingScheduler();
        var distributedLock = new PaymentDistributedLock(
            cache.Object,
            Options(new PaymentOptions { DistributedLockSeconds = 20 }),
            scheduler,
            NullLogger<PaymentDistributedLock>.Instance);

        var handle = await distributedLock.TryAcquireAsync(
            "resource-1", CancellationToken.None);
        handle.Should().NotBeNull();

        var act = async () => await handle!.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    private sealed class BlockingScheduler : IPaymentLockRenewalScheduler
    {
        public Task WaitForRenewalAsync(
            TimeSpan lease, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
