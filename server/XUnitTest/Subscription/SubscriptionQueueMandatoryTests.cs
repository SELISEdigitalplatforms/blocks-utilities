using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// The queue is the only way subscription background work runs.
/// </summary>
/// <remarks>
/// What these are really guarding is the absence of a second executor. The previous design chose
/// between the sweep executing work and the queue executing it, from a setting each read separately,
/// and both wrong answers cost money in opposite directions: one renewal charged twice, or none
/// charged at all.
/// </remarks>
public sealed class SubscriptionQueueMandatoryTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionPaymentLinkRepository> _links = new();
    private readonly Mock<ISubscriptionUsageInvoiceRepository> _invoices = new();
    private readonly Mock<ISubscriptionInvoiceHistoryRepository> _charges = new();
    private readonly Mock<ISubscriptionFinancialDocumentRepository> _documents = new();
    private readonly Mock<ISubscriptionDocumentCursorRepository> _cursors = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 27, 10, 3, 0, TimeSpan.Zero));

    private readonly List<SubscriptionWorkType> _announced = [];

    public SubscriptionQueueMandatoryTests()
    {
        // Every "is anything due" read answers "no" unless a test says otherwise. Moq's own default
        // for these is null, not an empty list, and the announcer counts what it gets — so an
        // unstubbed read throws rather than reporting nothing due, which would have every test here
        // fail for a reason unrelated to what it is about.
        _links
            .Setup(repository => repository.ListDueAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _invoices
            .Setup(repository => repository.ListDueAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subscriptions
            .Setup(repository => repository.ListStaleAsync(
                It.IsAny<string>(), It.IsAny<SubscriptionStatus>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subscriptions
            .Setup(repository => repository.ListStaleSettlementsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subscriptions
            .Setup(repository => repository.ListDueForRenewalAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subscriptions
            .Setup(repository => repository.ListDueForUsageRatingAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subscriptions
            .Setup(repository => repository.ListWithDueEventsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subscriptions
            .Setup(repository => repository.ListWithPendingDocumentSourcesAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _charges
            .Setup(repository => repository.ListSettledSinceAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _charges
            .Setup(repository => repository.ListRefundedSinceAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _documents
            .Setup(repository => repository.ListUndeliveredAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _scheduler
            .Setup(scheduler => scheduler.ScheduleAsync(
                It.IsAny<SubscriptionWorkType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback((
                SubscriptionWorkType workType,
                string _,
                string _,
                DateTime _,
                string _,
                string _,
                string? _,
                CancellationToken _) => _announced.Add(workType))
            .ReturnsAsync(true);
    }

    // ------------------------------------------------------------------ legacy configuration

    /// <summary>
    /// The setting that used to be able to stop the queue draining cannot any more.
    /// </summary>
    /// <remarks>
    /// It stays bindable for one release so a rollout carrying it does not fail on an unknown key.
    /// Ignored silently it would be worse than removed, because an operator would go on believing it
    /// did something &#8212; so the mandate says out loud what it read and what it is doing instead.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_legacy_scheduler_setting_is_read_reported_and_ignored(bool legacy)
    {
        var mandate = Mandate(new SubscriptionOptions
        {
            SchedulerEnabled = legacy,
            SchedulerCoordinationEnabled = legacy
        });

        mandate.LegacySchedulerEnabled.Should().Be(legacy);
        mandate.LegacyCoordinationEnabled.Should().Be(legacy);
    }

    [Fact]
    public void An_absent_legacy_setting_is_told_apart_from_one_set_to_false()
    {
        // Different things to whoever reads the warning: one is a deployment already cleaned up,
        // the other an operator who believes they have turned billing's execution path off.
        Mandate(new SubscriptionOptions()).LegacySchedulerEnabled.Should().BeNull();
        Mandate(new SubscriptionOptions { SchedulerEnabled = false })
            .LegacySchedulerEnabled.Should().BeFalse();
    }

    /// <summary>
    /// Nothing in the drainer's path can consult the retired setting, because nothing holds it.
    /// </summary>
    /// <remarks>
    /// Asserted against the type rather than by running a loop: the guarantee is structural, and a
    /// behavioural test would only prove the setting was ignored on the one path it exercised.
    /// </remarks>
    [Fact]
    public void The_drainer_takes_no_mode_or_gate_to_obey()
    {
        var parameters = typeof(global::Worker.SubscriptionWorkSchedulerBackgroundService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToList();

        parameters.Should().NotContain(name =>
            name.Contains("Mode", StringComparison.Ordinal) ||
            name.Contains("Gate", StringComparison.Ordinal) ||
            name.Contains("Coordinator", StringComparison.Ordinal));
    }

    /// <summary>The mode machinery is gone, not merely unused.</summary>
    /// <remarks>
    /// A dormant switch is an invitation to turn it back on. This fails if anybody reintroduces one,
    /// which is the point: the safety of every other test here rests on there being one executor.
    /// </remarks>
    [Fact]
    public void No_scheduler_mode_or_fleet_handover_type_survives()
    {
        var offenders = typeof(SubscriptionWorkQueue).Assembly
            .GetTypes()
            .Select(type => type.Name)
            .Where(name =>
                name.Contains("SchedulerMode", StringComparison.Ordinal) ||
                name.Contains("SchedulerFleet", StringComparison.Ordinal) ||
                name.Contains("SchedulerCoordinator", StringComparison.Ordinal) ||
                name.Contains("SchedulerRunMode", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ the repair sweep

    /// <summary>
    /// The sweep announces work and never runs it.
    /// </summary>
    /// <remarks>
    /// The announcer is constructed with no processor at all, so this is enforced by what it can
    /// reach rather than by what it happens to call. Proving it that way is why the logic was lifted
    /// out of the hosted service: a timer loop cannot be asked whether it charged anybody.
    /// </remarks>
    [Fact]
    public void The_repair_announcer_can_reach_no_processor_to_execute_work_with()
    {
        var dependencies = typeof(SubscriptionRepairAnnouncer)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        // Named individually rather than by a "Processor" substring, so renaming a processor cannot
        // quietly retire the assertion.
        dependencies.Should().NotContain(typeof(ISubscriptionActivationProcessor));
        dependencies.Should().NotContain(typeof(ISubscriptionRenewalProcessor));
        dependencies.Should().NotContain(typeof(ISubscriptionUsageRatingProcessor));
        dependencies.Should().NotContain(typeof(ISubscriptionSettlementReservationProcessor));
        dependencies.Should().NotContain(typeof(ISubscriptionOutboxProcessor));

        // And no service locator either: given one, it could resolve any of the above.
        dependencies.Should().NotContain(typeof(IServiceProvider));
    }

    [Fact]
    public async Task Due_renewal_usage_and_document_work_is_announced_rather_than_run()
    {
        DueForRenewal();
        DueForUsageClosure();
        OwesADocument();

        var announced = await Announcer().AnnounceAsync(TenantId, CancellationToken.None);

        announced.Should().Be(3);
        _announced.Should().BeEquivalentTo(new[]
        {
            SubscriptionWorkType.Renewal,
            SubscriptionWorkType.UsagePeriodClosure,
            SubscriptionWorkType.FinancialDocumentIssue
        });
    }

    [Fact]
    public async Task A_tenant_owing_nothing_is_announced_for_nothing()
    {
        var announced = await Announcer().AnnounceAsync(TenantId, CancellationToken.None);

        // Not one empty item per work type. Writing seven of those per tenant per bucket would put
        // the roster scan back into the production path and leave an idle fleet looking busy.
        announced.Should().Be(0);
        _announced.Should().BeEmpty();
    }

    /// <summary>
    /// Two announcements of the same thing resolve to one occurrence.
    /// </summary>
    /// <remarks>
    /// Bucketed by wall clock, so a sweep overlapping itself &#8212; or two replicas sweeping the same
    /// roster &#8212; asks for the same work key. The queue's unique occurrence index collapses them;
    /// what this pins is that the key is stable across passes, without which the index has nothing to
    /// collapse on.
    /// </remarks>
    [Fact]
    public async Task Repeated_announcements_within_a_bucket_use_one_work_key()
    {
        DueForRenewal();

        var keys = new List<string>();
        _scheduler
            .Setup(scheduler => scheduler.ScheduleAsync(
                It.IsAny<SubscriptionWorkType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback((
                SubscriptionWorkType _,
                string _,
                string workKey,
                DateTime _,
                string _,
                string _,
                string? _,
                CancellationToken _) => keys.Add(workKey))
            .ReturnsAsync(true);

        var announcer = Announcer();

        await announcer.AnnounceAsync(TenantId, CancellationToken.None);

        // A minute later, inside the same five-minute bucket.
        _time.Advance(TimeSpan.FromMinutes(1));
        await announcer.AnnounceAsync(TenantId, CancellationToken.None);

        keys.Should().HaveCount(2);
        keys.Distinct().Should().ContainSingle("both passes fall in one bucket");

        // And the next bucket is a new occurrence, or a tenant that stays due would never be
        // announced again after the first pass.
        _time.Advance(TimeSpan.FromMinutes(5));
        await announcer.AnnounceAsync(TenantId, CancellationToken.None);

        keys.Distinct().Should().HaveCount(2);
    }

    // ------------------------------------------------------------------ readiness

    /// <summary>
    /// Reaching the database is not evidence that anything is draining.
    /// </summary>
    /// <remarks>
    /// This is the defect the fleet registry exists for. The health check runs in the Api and the
    /// drainer runs in the Worker, so a per-process readiness object read from the Api was always in
    /// its pristine starting state — and the endpoint reported "work is draining" on the strength of
    /// the Api's own connection to MongoDB. Every replica could be dead with renewals piling up
    /// unclaimed, and it said healthy.
    /// </remarks>
    [Fact]
    public async Task A_reachable_queue_with_no_live_drainer_is_unhealthy()
    {
        var result = await Health(Reachable(), Fleet(live: 0, draining: 0))
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("reachable, which is not the same as being drained");
    }

    /// <summary>
    /// A replica that is up but cannot claim is reported apart from one that is gone.
    /// </summary>
    /// <remarks>
    /// The two point somewhere different: alive-and-failing is a database problem, absent is a
    /// deployment problem. Reporting them the same way sends somebody to the wrong place.
    /// </remarks>
    [Fact]
    public async Task A_live_drainer_that_cannot_claim_is_unhealthy_and_says_so_differently()
    {
        var result = await Health(Reachable(), Fleet(live: 2, draining: 0))
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("alive but none has claimed");
        result.Description.Should().NotContain("has reported in for");
    }

    [Fact]
    public async Task A_fleet_that_is_claiming_is_healthy()
    {
        var result = await Health(Reachable(), Fleet(live: 3, draining: 3))
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("3 subscription work drainer(s) are claiming");
    }

    [Fact]
    public async Task One_replica_retrying_is_degraded_rather_than_an_incident()
    {
        // Something is draining. A failover drops a pass or two, and paging on it teaches people to
        // ignore the page.
        var result = await Health(Reachable(), Fleet(live: 3, draining: 2, worstFailures: 2))
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    /// <summary>
    /// Not being able to tell is not evidence that things are fine.
    /// </summary>
    /// <remarks>
    /// The failure mode this replaces treated an unknown as a healthy. An unreadable registry means
    /// nobody knows whether work is being executed, which is worth reporting as such.
    /// </remarks>
    [Fact]
    public async Task An_unreadable_worker_registry_is_unhealthy_not_assumed_well()
    {
        var workers = new Mock<ISubscriptionQueueWorkerRegistry>();
        workers
            .Setup(registry => registry.DescribeFleetAsync(
                It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("no answer"));

        var result = await new SubscriptionQueueHealthCheck(
                Reachable().Object,
                workers.Object,
                new OptionsMonitorStub(new SubscriptionOptions()),
                _time)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("could not be established");
    }

    /// <summary>
    /// A root-database outage reports unready rather than routing the work elsewhere.
    /// </summary>
    /// <remarks>
    /// This is the trade the change makes explicit. Removing the fallback removes the double-charge
    /// risk and leaves an outage that bills nobody, so the outage has to be loud.
    /// </remarks>
    [Fact]
    public async Task An_unreachable_root_database_is_unhealthy_and_never_a_fallback()
    {
        var queue = new Mock<ISubscriptionWorkQueue>();
        queue
            .Setup(work => work.ProbeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionWorkQueueProbe(
                RootDatabaseReachable: false,
                MissingIndexes: [],
                ClaimQueryable: false,
                Error: "No connection could be made"));

        var result = await Health(queue, Fleet(live: 1, draining: 1))
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("no subscription");
    }

    [Fact]
    public async Task A_queue_missing_its_occurrence_index_refuses_to_be_called_ready()
    {
        var queue = new Mock<ISubscriptionWorkQueue>();
        queue
            .Setup(work => work.ProbeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionWorkQueueProbe(
                RootDatabaseReachable: true,
                MissingIndexes: [SubscriptionWorkIndexNames.Occurrence],
                ClaimQueryable: true,
                Error: null));

        var result = await Health(queue, Fleet(live: 1, draining: 1))
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Without it two producers can create two items for one billing period, which is two
        // chances to charge. Draining then is worse than draining nothing.
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain(SubscriptionWorkIndexNames.Occurrence);
    }

    /// <summary>
    /// The connectivity check says what it proves, and does not claim readiness.
    /// </summary>
    /// <remarks>
    /// Kept as a separate check for exactly that reason. Reporting connectivity under the readiness
    /// tag is the conflation that produced a healthy endpoint over a stopped fleet.
    /// </remarks>
    [Fact]
    public async Task The_connectivity_check_does_not_claim_anything_is_draining()
    {
        var result = await new SubscriptionQueueConnectivityHealthCheck(Reachable().Object)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("says nothing about whether a drainer is running");
        result.Data["proves"].Should().Be(
            "queue connectivity only, not that anything is draining");
    }

    // ------------------------------------------------------------------ coverage of the work types

    /// <summary>
    /// Every kind of subscription background work has a queue handler.
    /// </summary>
    /// <remarks>
    /// The queue is now the only path, so a work type without a handler is work that can be
    /// announced and never run &#8212; a renewal that queues, dead-letters and is never charged.
    /// Previously the sweep would have executed it regardless, which is what made this gap
    /// survivable and therefore easy to miss.
    /// </remarks>
    [Fact]
    public void Every_work_type_has_a_handler_because_nothing_else_would_run_it()
    {
        var handlers = typeof(SubscriptionWorkQueue).Assembly
            .GetTypes()
            .Where(type => typeof(ISubscriptionWorkHandler).IsAssignableFrom(type)
                && type is { IsAbstract: false, IsInterface: false })
            .ToList();

        handlers.Should().NotBeEmpty("the queue is the only executor, so it needs handlers");

        // Matched by type name rather than by constructing a handler to read its WorkType, which
        // would need every processor that handler depends on. Names the missing type when it fails.
        var missing = Enum.GetValues<SubscriptionWorkType>()
            .Where(workType => !handlers.Any(handler =>
                handler.Name.Contains(workType.ToString(), StringComparison.Ordinal)))
            .ToList();

        missing.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ helpers

    private SubscriptionQueueMandate Mandate(SubscriptionOptions options) =>
        new(
            Options.Create(options),
            NullLogger<SubscriptionQueueMandate>.Instance);

    private SubscriptionRepairAnnouncer Announcer() => new(
        _scheduler.Object,
        _subscriptions.Object,
        _links.Object,
        _invoices.Object,
        _charges.Object,
        _documents.Object,
        _cursors.Object,
        new OptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<SubscriptionRepairAnnouncer>.Instance,
        _time);

    private SubscriptionQueueHealthCheck Health(
        Mock<ISubscriptionWorkQueue> queue,
        Mock<ISubscriptionQueueWorkerRegistry> workers) =>
        new(
            queue.Object,
            workers.Object,
            new OptionsMonitorStub(new SubscriptionOptions()),
            _time);

    /// <summary>A fleet reading, as the registry in the root database would report it.</summary>
    private Mock<ISubscriptionQueueWorkerRegistry> Fleet(
        int live,
        int draining,
        int worstFailures = 0)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var workers = new Mock<ISubscriptionQueueWorkerRegistry>();

        workers
            .Setup(registry => registry.DescribeFleetAsync(
                It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionQueueFleetHealth(
                live,
                draining,
                live > 0 ? now : null,
                draining > 0 ? now : null,
                worstFailures,
                worstFailures > 0 ? "queue_pass_failed" : null));

        return workers;
    }

    private static Mock<ISubscriptionWorkQueue> Reachable()
    {
        var queue = new Mock<ISubscriptionWorkQueue>();

        queue
            .Setup(work => work.ProbeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionWorkQueueProbe(true, [], true, null));

        return queue;
    }

    private void DueForRenewal() =>
        _subscriptions
            .Setup(repository => repository.ListDueForRenewalAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubscriptionDetail { ItemId = "sub-1" }]);

    private void DueForUsageClosure() =>
        _subscriptions
            .Setup(repository => repository.ListDueForUsageRatingAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubscriptionDetail { ItemId = "sub-1" }]);

    private void OwesADocument() =>
        _subscriptions
            .Setup(repository => repository.ListWithPendingDocumentSourcesAsync(
                TenantId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubscriptionDetail { ItemId = "sub-1" }]);

    /// <summary>An options monitor that answers one value, which is all these need.</summary>
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
