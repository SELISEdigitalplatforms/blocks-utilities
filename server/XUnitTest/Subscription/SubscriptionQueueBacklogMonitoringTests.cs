using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;
using Worker;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Backlog monitoring has to keep working while the queue is busy.
/// </summary>
/// <remarks>
/// Depth was reported only after an <em>empty</em> batch. With something to claim on every pass the
/// report never ran, so the one queue shape worth alerting on &#8212; a backlog that keeps growing
/// &#8212; produced no fresh numbers.
/// <para>
/// It is not a hypothetical shape. `FinancialDocumentIssue` and `FinancialDocumentDelivery` are the
/// lowest-priority work types on purpose, so a sustained run of renewals and recovery starves them
/// first: paid transactions with no invoice issued, and issued invoices never delivered, while the
/// dashboard shows whatever the last idle pass measured.
/// </para>
/// </remarks>
public sealed class SubscriptionQueueBacklogMonitoringTests
{
    private readonly Mock<ISubscriptionWorkDispatcher> _dispatcher = new();
    private readonly Mock<ISubscriptionWorkQueue> _queue = new();
    private readonly Mock<ISubscriptionQueueWorkerRegistry> _workers = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));

    private int _depthReads;

    public SubscriptionQueueBacklogMonitoringTests()
    {
        _queue
            .Setup(queue => queue.EnsureIndexesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _queue
            .Setup(queue => queue.DescribeDepthAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref _depthReads))
            .ReturnsAsync(
            [
                new SubscriptionWorkQueueDepth(
                    SubscriptionWorkType.FinancialDocumentIssue,
                    BackgroundWorkStatus.Pending,
                    12,
                    _time.GetUtcNow().UtcDateTime.AddHours(-2))
            ]);
    }

    /// <summary>
    /// Every batch full, and depth is still measured.
    /// </summary>
    /// <remarks>
    /// The dispatcher always reports work done, which is the sustained-load case. Before, that path
    /// went straight back to claiming and never measured anything.
    /// </remarks>
    [Fact]
    public async Task Depth_is_reported_during_sustained_non_empty_processing()
    {
        _dispatcher
            .Setup(dispatcher => dispatcher.ProcessDueAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5)
            // The clock has to move for an interval-driven report to come due, and a busy loop's
            // passes are what move it here.
            .Callback(() => _time.Advance(TimeSpan.FromSeconds(20)));

        using var cancellation = new CancellationTokenSource();
        var service = Service();

        await service.StartAsync(cancellation.Token);

        // Long enough for several passes at 20 simulated seconds each, against a 30 second report
        // interval.
        await WaitUntilAsync(() => _depthReads >= 2, TimeSpan.FromSeconds(10));

        await cancellation.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        _depthReads.Should().BeGreaterThanOrEqualTo(
            2,
            "a queue that always has work must still report its own backlog");

        // And the work kept being drained rather than being traded for monitoring.
        _dispatcher.Verify(
            dispatcher => dispatcher.ProcessDueAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    /// <summary>
    /// A drainer publishes its liveness where another process can read it.
    /// </summary>
    /// <remarks>
    /// The health check runs in the Api and this loop runs in the Worker; they share no memory. This
    /// record is the only thing that crosses that boundary, and without it readiness was answering
    /// from an object nobody had written.
    /// </remarks>
    [Fact]
    public async Task The_drainer_publishes_a_heartbeat_another_process_can_read()
    {
        _dispatcher
            .Setup(dispatcher => dispatcher.ProcessDueAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var beats = new List<SubscriptionQueueWorkerBeat>();
        _workers
            .Setup(registry => registry.HeartbeatAsync(
                It.IsAny<SubscriptionQueueWorkerBeat>(), It.IsAny<CancellationToken>()))
            .Callback((SubscriptionQueueWorkerBeat beat, CancellationToken _) => beats.Add(beat))
            .Returns(Task.CompletedTask);

        using var cancellation = new CancellationTokenSource();
        var service = Service();

        await service.StartAsync(cancellation.Token);
        await WaitUntilAsync(() => beats.Count >= 1, TimeSpan.FromSeconds(10));

        await cancellation.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        beats.Should().NotBeEmpty();

        // Distinct per process start: process ids are reused, and a pod restarting into the same id
        // would otherwise inherit the dead process's failure history.
        beats[0].WorkerId.Should().Contain(Environment.MachineName);
        beats[0].WorkerId.Length.Should().BeGreaterThan(Environment.MachineName.Length + 1);
    }

    /// <summary>
    /// A heartbeat that cannot be written never takes the billing loop down with it.
    /// </summary>
    /// <remarks>
    /// It reports itself by its absence, which is exactly what readiness looks for. Throwing here
    /// would stop the work in order to announce that the work is unhealthy.
    /// </remarks>
    [Fact]
    public async Task A_failing_heartbeat_does_not_stop_the_drainer()
    {
        _workers
            .Setup(registry => registry.HeartbeatAsync(
                It.IsAny<SubscriptionQueueWorkerBeat>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("registry unavailable"));

        var passes = 0;
        _dispatcher
            .Setup(dispatcher => dispatcher.ProcessDueAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)
            .Callback(() =>
            {
                Interlocked.Increment(ref passes);
                _time.Advance(TimeSpan.FromSeconds(20));
            });

        using var cancellation = new CancellationTokenSource();
        var service = Service();

        await service.StartAsync(cancellation.Token);
        await WaitUntilAsync(() => passes >= 3, TimeSpan.FromSeconds(10));

        await cancellation.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        passes.Should().BeGreaterThanOrEqualTo(3);
    }

    private SubscriptionWorkSchedulerBackgroundService Service() => new(
        _dispatcher.Object,
        _queue.Object,
        _workers.Object,
        new OptionsMonitorStub(new SubscriptionOptions
        {
            // Zero-ish so a test does not wait a real poll interval; the floors in the service keep
            // production honest.
            SchedulerPollSeconds = 1,
            SchedulerDepthReportSeconds = 30,
            SchedulerWorkerHeartbeatSeconds = 5
        }),
        new SubscriptionQueueMandate(
            Options.Create(new SubscriptionOptions()),
            NullLogger<SubscriptionQueueMandate>.Instance),
        new SubscriptionQueueReadiness(),
        new SubscriptionWorkMetrics(),
        NullLogger<SubscriptionWorkSchedulerBackgroundService>.Instance,
        _time);

    /// <summary>
    /// Polls a condition rather than sleeping a fixed time.
    /// </summary>
    /// <remarks>
    /// A fixed sleep is either flaky on a loaded machine or slow on an idle one. This is the loop
    /// under test running on its own thread, so the only honest way to wait is for what it does.
    /// </remarks>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }
    }

    private sealed class OptionsMonitorStub : IOptionsMonitor<SubscriptionOptions>
    {
        public OptionsMonitorStub(SubscriptionOptions value) => CurrentValue = value;

        public SubscriptionOptions CurrentValue { get; }

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<SubscriptionOptions, string?> listener) =>
            new Unsubscriber();

        private sealed class Unsubscriber : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
