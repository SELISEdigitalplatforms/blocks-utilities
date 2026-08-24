using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Services;
using Payment.DomainService.Enums;
using Payment.DomainService.Scheduling;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Claiming due work and deciding what happens to it afterwards.
/// </summary>
/// <remarks>
/// The queue itself is exercised against a real MongoDB in
/// <c>SubscriptionWorkQueueIntegrationTests</c> — atomic claims and lease expiry are properties of
/// the database, not of this class. What is asserted here is the half a fake can prove: that a
/// completed handler completes the item, that a transient failure goes back to the queue and a
/// permanent one does not, and that nothing runs outside an established tenant context.
/// </remarks>
public sealed class PaymentBackgroundWorkDispatcherTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<IPaymentWorkQueue> _queue = new();
    private readonly Mock<IPaymentTenantContextScopeFactory> _tenantContext = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));

    private readonly List<PaymentBackgroundWork> _claimed = [];
    private readonly RecordingHandler _handler = new(PaymentWorkType.PaymentRecovery);

    public PaymentBackgroundWorkDispatcherTests()
    {
        _tenantContext
            .Setup(factory => factory.Establish(It.IsAny<string>()))
            .Returns(new NoopScope());

        _queue
            .Setup(queue => queue.ClaimDueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _claimed);

        _queue
            .Setup(queue => queue.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _queue
            .Setup(queue => queue.RenewLeaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _queue
            .Setup(queue => queue.FailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BackgroundWorkStatus.Pending);
    }

    [Fact]
    public async Task Nothing_due_claims_nothing_and_runs_nothing()
    {
        var processed = await Dispatcher().ProcessDueAsync("worker-1", default);

        processed.Should().Be(0);
        _handler.Executions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_claimed_item_runs_and_is_completed_under_the_lease_that_claimed_it()
    {
        _claimed.Add(Work());

        string? claimLease = null;
        _queue
            .Setup(queue => queue.ClaimDueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback((string leaseId, string _, int _, TimeSpan _, CancellationToken _) =>
                claimLease = leaseId)
            .ReturnsAsync(() => _claimed);

        var processed = await Dispatcher().ProcessDueAsync("worker-1", default);

        processed.Should().Be(1);
        _handler.Executions.Should().ContainSingle();

        // Completed under the same lease it was claimed with: an attempt whose lease has been taken
        // over must not be able to close the item on the new holder's behalf.
        _queue.Verify(
            queue => queue.CompleteAsync(
                "work-1", claimLease!, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_tenant_context_is_established_before_a_handler_reads_anything()
    {
        _claimed.Add(Work());

        await Dispatcher().ProcessDueAsync("worker-1", default);

        // Background work has no request to carry a tenant, and a handler that read one tenant's
        // database under another's context would be a cross-tenant read of financial state.
        _tenantContext.Verify(factory => factory.Establish(TenantId), Times.Once);
    }

    [Fact]
    public async Task A_transient_failure_goes_back_to_the_queue_with_backoff()
    {
        _claimed.Add(Work());
        _handler.Outcome = PaymentWorkOutcome.Retry("provider_unreachable", "No answer.");

        var processed = await Dispatcher().ProcessDueAsync("worker-1", default);

        processed.Should().Be(0);
        _queue.Verify(
            queue => queue.FailAsync(
                "work-1",
                It.IsAny<string>(),
                "provider_unreachable",
                It.IsAny<string>(),
                false,
                It.Is<TimeSpan>(backoff => backoff > TimeSpan.Zero),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _queue.Verify(
            queue => queue.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_permanent_failure_is_dead_lettered_rather_than_retried()
    {
        _claimed.Add(Work());
        _handler.Outcome = PaymentWorkOutcome.Permanent("subscription_not_found", "Gone.");

        await Dispatcher().ProcessDueAsync("worker-1", default);

        // Spending five attempts proving the same subscription is still missing is five chances for
        // something else to go wrong, and no chance of success.
        _queue.Verify(
            queue => queue.FailAsync(
                "work-1", It.IsAny<string>(), "subscription_not_found", It.IsAny<string>(),
                true, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_handler_that_throws_is_retried_rather_than_lost()
    {
        _claimed.Add(Work());
        _handler.Throw = new InvalidOperationException("boom");

        var processed = await Dispatcher().ProcessDueAsync("worker-1", default);

        processed.Should().Be(0);
        _queue.Verify(
            queue => queue.FailAsync(
                "work-1", It.IsAny<string>(), "unhandled_exception", "InvalidOperationException",
                false, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Work_this_build_cannot_run_is_dead_lettered_rather_than_retried_forever()
    {
        // A work type added by a later deployment. Retrying it every thirty seconds until attempts
        // run out is just a slower way of dead-lettering it, with noise.
        _claimed.Add(Work(workType: PaymentWorkType.RefundRecovery));

        await Dispatcher().ProcessDueAsync("worker-1", default);

        _queue.Verify(
            queue => queue.FailAsync(
                "work-1", It.IsAny<string>(), "work_type_unhandled", It.IsAny<string>(),
                true, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Every_item_in_a_batch_runs()
    {
        _claimed.AddRange([Work("work-1"), Work("work-2"), Work("work-3")]);

        var processed = await Dispatcher().ProcessDueAsync("worker-1", default);

        processed.Should().Be(3);
        _handler.Executions.Should().HaveCount(3);
    }

    [Fact]
    public async Task The_batch_size_and_lease_come_from_configuration()
    {
        await Dispatcher(batchSize: 7, leaseSeconds: 300).ProcessDueAsync("worker-1", default);

        _queue.Verify(
            queue => queue.ClaimDueAsync(
                It.IsAny<string>(),
                "worker-1",
                7,
                TimeSpan.FromSeconds(300),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Completion_that_the_queue_refuses_is_not_reported_as_success()
    {
        // The lease moved between the last renewal and the write. The work succeeded, but this
        // attempt does not own the item — and counting it as processed is how an item that ran
        // twice looks like one that ran once.
        _claimed.Add(Work());
        _queue
            .Setup(queue => queue.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var processed = await Dispatcher().ProcessDueAsync("worker-1", default);

        processed.Should().Be(0);
    }

    // The subscription suite's audit tests are absent, not forgotten: this module has no audit
    // trail for an abandonment to be recorded against. See Scheduling/README.md.

    [Fact]
    public async Task A_long_running_handler_keeps_its_lease_alive()
    {
        // Without renewal, work that outlives its lease is reclaimed and run a second time while
        // the first attempt is still inside a provider call.
        _claimed.Add(Work());

        var renewed = new TaskCompletionSource();
        _handler.WaitFor = renewed.Task;
        _queue
            .Setup(queue => queue.RenewLeaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => renewed.TrySetResult())
            .ReturnsAsync(true);

        await Dispatcher(renewalInterval: TimeSpan.FromMilliseconds(50))
            .ProcessDueAsync("worker-1", default);

        // Renewed at half the lease in production; shortened here, because a test cannot wait a
        // minute to watch a renewal that a real handler triggers by taking that long.
        _queue.Verify(
            queue => queue.RenewLeaseAsync(
                "work-1", It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Losing_the_lease_mid_flight_stops_the_handler_and_records_nothing()
    {
        _claimed.Add(Work());
        _handler.Delay = TimeSpan.FromMilliseconds(600);
        _queue
            .Setup(queue => queue.RenewLeaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var processed = await Dispatcher(renewalInterval: TimeSpan.FromMilliseconds(50))
            .ProcessDueAsync("worker-1", default);

        processed.Should().Be(0);

        // Neither completed nor failed: whoever holds the item now decides it, and writing either
        // from here would overwrite their outcome with a stale one.
        _queue.Verify(
            queue => queue.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _queue.Verify(
            queue => queue.FailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // And the handler was asked to stop rather than left running beside the new holder.
        _handler.Cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task A_renewal_that_keeps_throwing_stops_the_handler_once_the_lease_can_no_longer_be_proven()
    {
        // A failed renewal call is not proof the lease is gone — but time passing is. Retried while
        // the last confirmed lease still covers the attempt, and treated as lost after that: a
        // handler running past an expiry nobody extended is one another worker may have reclaimed.
        _claimed.Add(Work());
        _handler.Delay = TimeSpan.FromSeconds(5);
        _queue
            .Setup(queue => queue.RenewLeaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("root database unreachable"));

        // A 400ms lease keeps 100ms in reserve, so the deadline is 300ms after the claim — real
        // time, no clock to move, and the same arithmetic production uses.
        var processed = await Dispatcher(
                renewalInterval: TimeSpan.FromMilliseconds(50),
                lease: TimeSpan.FromMilliseconds(400),
                time: TimeProvider.System)
            .ProcessDueAsync("worker-1", default);

        processed.Should().Be(0);
        _handler.Cancelled.Should().BeTrue();

        // And nothing was written: the item belongs to whoever holds it now.
        _queue.Verify(
            queue => queue.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _queue.Verify(
            queue => queue.FailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_renewal_that_never_answers_stops_the_handler_at_the_safety_deadline()
    {
        // The failure the earlier version could not survive: a call that neither succeeds nor
        // throws. Waiting on it, the lease expired while the handler ran on, and another worker
        // could reclaim and run the same item. Safety cannot depend on the database answering.
        _claimed.Add(Work());
        _handler.Delay = TimeSpan.FromSeconds(5);

        var neverAnswers = new TaskCompletionSource<bool>();
        _queue
            .Setup(queue => queue.RenewLeaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns(neverAnswers.Task);

        var processed = await Dispatcher(
                renewalInterval: TimeSpan.FromMilliseconds(20),
                lease: TimeSpan.FromMilliseconds(400),
                time: TimeProvider.System)
            .ProcessDueAsync("worker-1", default);

        processed.Should().Be(0);
        _handler.Cancelled.Should().BeTrue("the handler must stop before the item can be reclaimed");

        // Neither outcome is this attempt's to record: it can no longer prove it owns the item.
        _queue.Verify(
            queue => queue.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _queue.Verify(
            queue => queue.FailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_renewal_is_never_asked_for_without_a_cancellation_token()
    {
        // A renewal that can outlive the lease it is renewing is a call whose answer cannot mean
        // anything by the time it arrives.
        _claimed.Add(Work());

        var renewed = new TaskCompletionSource();
        // The handler runs until a renewal has actually happened, so this cannot pass or fail on
        // whether a background task got a time slice.
        _handler.WaitFor = renewed.Task;

        CancellationToken captured = default;
        _queue
            .Setup(queue => queue.RenewLeaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback((string _, string _, TimeSpan _, CancellationToken token) =>
            {
                captured = token;
                renewed.TrySetResult();
            })
            .ReturnsAsync(true);

        await Dispatcher(
                renewalInterval: TimeSpan.FromMilliseconds(30),
                lease: TimeSpan.FromSeconds(30))
            .ProcessDueAsync("worker-1", default);

        captured.CanBeCanceled.Should().BeTrue();
    }

    private PaymentBackgroundWorkDispatcher Dispatcher(
        int batchSize = 20,
        int leaseSeconds = 120,
        TimeSpan? renewalInterval = null,
        TimeSpan? lease = null,
        TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_tenantContext.Object);
        services.AddSingleton<IPaymentWorkHandler>(_handler);

        var options = new PaymentOptions
        {
            SchedulerBatchSize = batchSize,
            SchedulerLeaseSeconds = leaseSeconds,
            SchedulerMaxParallelism = 2
        };

        return new PaymentBackgroundWorkDispatcher(
            _queue.Object,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new PaymentOptionsMonitorStub(options),
            NullLogger<PaymentBackgroundWorkDispatcher>.Instance,
            time ?? _time,
            renewalInterval,
            lease,
            metrics: null);
    }

    private static PaymentBackgroundWork Work(
        string itemId = "work-1",
        PaymentWorkType workType = PaymentWorkType.PaymentRecovery) => new()
    {
        ItemId = itemId,
        TenantId = TenantId,
        WorkType = workType,
        WorkKey = "sweep:20260823T1200Z",
        Status = BackgroundWorkStatus.Processing,
        DueAtUtc = new DateTime(2026, 8, 23, 11, 55, 0, DateTimeKind.Utc),
        NextAttemptAtUtc = new DateTime(2026, 8, 23, 11, 55, 0, DateTimeKind.Utc),
        AttemptCount = 1,
        CorrelationId = "corr-1"
    };

    private sealed class RecordingHandler : IPaymentWorkHandler
    {
        public RecordingHandler(PaymentWorkType workType) => WorkType = workType;

        public PaymentWorkType WorkType { get; }

        public List<PaymentBackgroundWork> Executions { get; } = [];

        public PaymentWorkOutcome Outcome { get; set; } = PaymentWorkOutcome.Completed();

        public Exception? Throw { get; set; }

        /// <summary>How long this handler pretends to be busy, so a lease can expire under it.</summary>
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Something to wait for instead of a duration. A test asserting that the lease was renewed
        /// waits for the renewal itself rather than for a window long enough to contain it — under
        /// load, "long enough" is not a property a delay has.
        /// </summary>
        public Task? WaitFor { get; set; }


        public bool Cancelled { get; private set; }

        public async Task<PaymentWorkOutcome> ExecuteAsync(
            PaymentBackgroundWork work,
            CancellationToken cancellationToken)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            lock (Executions)
            {
                Executions.Add(work);
            }

            if (WaitFor is not null)
            {
                try
                {
                    await WaitFor.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Cancelled = true;
                }
            }
            else if (Delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(Delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // What a real handler does when its lease is pulled: stop.
                    Cancelled = true;
                }
            }

            return Outcome;
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
