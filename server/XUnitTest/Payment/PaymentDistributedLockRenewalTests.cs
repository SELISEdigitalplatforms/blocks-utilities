using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace XUnitTest.Payment;

public sealed class PaymentDistributedLockRenewalTests
{
    [Fact]
    public async Task Acquired_lock_renews_automatically_and_releases_only_after_renewal_stops()
    {
        var database = new Mock<IDatabase>();
        database
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                When.NotExists))
            .ReturnsAsync(true);

        var renewalObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.Is<string>(script => script.Contains("pexpire")),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                CommandFlags.None))
            .Callback(() => renewalObserved.TrySetResult())
            .ReturnsAsync(RedisResult.Create((RedisValue)1));
        database
            .Setup(x => x.ScriptEvaluateAsync(
                It.Is<string>(script => script.Contains("del")),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                CommandFlags.None))
            .ReturnsAsync(RedisResult.Create((RedisValue)1));

        var cacheClient = new Mock<ICacheClient>();
        cacheClient.Setup(x => x.CacheDatabase()).Returns(database.Object);
        var scheduler = new ImmediateThenBlockingScheduler();
        var distributedLock = new PaymentDistributedLock(
            cacheClient.Object,
            OptionsMonitor(new PaymentOptions
            {
                DistributedLockSeconds = 20,
                DistributedLockWaitMilliseconds = 0
            }),
            scheduler,
            NullLogger<PaymentDistributedLock>.Instance);

        var handle = await distributedLock.TryAcquireAsync("resource-1", CancellationToken.None);
        handle.Should().NotBeNull();
        await renewalObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await handle!.DisposeAsync();

        scheduler.CallCount.Should().BeGreaterThanOrEqualTo(2);
        database.Verify(x => x.ScriptEvaluateAsync(
            It.Is<string>(script => script.Contains("pexpire")),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            CommandFlags.None), Times.Once);
        database.Verify(x => x.ScriptEvaluateAsync(
            It.Is<string>(script => script.Contains("del")),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task Acquisition_preserves_caller_cancellation()
    {
        var cacheClient = new Mock<ICacheClient>(MockBehavior.Strict);
        var scheduler = new Mock<IPaymentLockRenewalScheduler>(MockBehavior.Strict);
        var distributedLock = new PaymentDistributedLock(
            cacheClient.Object,
            OptionsMonitor(new PaymentOptions()),
            scheduler.Object,
            NullLogger<PaymentDistributedLock>.Instance);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () => distributedLock.TryAcquireAsync("resource-1", cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        cacheClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Renewal_scheduler_waits_for_one_third_of_the_lease()
    {
        var scheduler = new PaymentLockRenewalScheduler();
        using var cancellation = new CancellationTokenSource();
        var started = DateTime.UtcNow;

        var wait = scheduler.WaitForRenewalAsync(TimeSpan.FromSeconds(30), cancellation.Token);
        await Task.Delay(30);
        wait.IsCompleted.Should().BeFalse();
        await cancellation.CancelAsync();

        var action = async () => await wait;
        await action.Should().ThrowAsync<OperationCanceledException>();
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    private static IOptionsMonitor<PaymentOptions> OptionsMonitor(PaymentOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(x => x.CurrentValue).Returns(options);
        return monitor.Object;
    }

    private sealed class ImmediateThenBlockingScheduler : IPaymentLockRenewalScheduler
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task WaitForRenewalAsync(TimeSpan lease, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
                return Task.CompletedTask;

            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
