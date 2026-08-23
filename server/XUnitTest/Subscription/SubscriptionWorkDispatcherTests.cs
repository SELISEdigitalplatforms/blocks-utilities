using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Services;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

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
public sealed class SubscriptionWorkDispatcherTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionWorkQueue> _queue = new();
    private readonly Mock<IPaymentTenantContextScopeFactory> _tenantContext = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));

    private readonly List<SubscriptionBackgroundWork> _claimed = [];
    private readonly RecordingHandler _handler = new(SubscriptionWorkType.Renewal);

    public SubscriptionWorkDispatcherTests()
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
        _handler.Outcome = SubscriptionWorkOutcome.Retry("provider_unreachable", "No answer.");

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
        _handler.Outcome = SubscriptionWorkOutcome.Permanent("subscription_not_found", "Gone.");

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
        _claimed.Add(Work(workType: SubscriptionWorkType.UsageInvoiceCharge));

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

    private SubscriptionWorkDispatcher Dispatcher(int batchSize = 20, int leaseSeconds = 120)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_tenantContext.Object);
        services.AddSingleton<ISubscriptionWorkHandler>(_handler);

        var options = new SubscriptionOptions
        {
            SchedulerBatchSize = batchSize,
            SchedulerLeaseSeconds = leaseSeconds,
            SchedulerMaxParallelism = 2
        };

        return new SubscriptionWorkDispatcher(
            _queue.Object,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new SubscriptionOptionsMonitorStub(options),
            NullLogger<SubscriptionWorkDispatcher>.Instance,
            _time);
    }

    private static SubscriptionBackgroundWork Work(
        string itemId = "work-1",
        SubscriptionWorkType workType = SubscriptionWorkType.Renewal) => new()
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

    private sealed class RecordingHandler : ISubscriptionWorkHandler
    {
        public RecordingHandler(SubscriptionWorkType workType) => WorkType = workType;

        public SubscriptionWorkType WorkType { get; }

        public List<SubscriptionBackgroundWork> Executions { get; } = [];

        public SubscriptionWorkOutcome Outcome { get; set; } = SubscriptionWorkOutcome.Completed();

        public Exception? Throw { get; set; }

        public Task<SubscriptionWorkOutcome> ExecuteAsync(
            SubscriptionBackgroundWork work,
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

            return Task.FromResult(Outcome);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
