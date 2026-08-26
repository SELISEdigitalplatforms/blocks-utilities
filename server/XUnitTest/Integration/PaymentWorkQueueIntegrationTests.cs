using Blocks.Genesis;
using MongoDB.Driver;
using FluentAssertions;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Scheduling;
using XUnitTest.Payment;

namespace XUnitTest.Integration;

/// <summary>
/// The payment queue's guarantees, against a real MongoDB.
/// </summary>
/// <remarks>
/// These are properties of the database rather than of any class: an atomic claim, a unique
/// occurrence, a lease that expires. A fake queue can be made to exhibit all three and prove
/// nothing, which is why they live here.
/// <para>
/// Needs a reachable mongod, or <c>BLOCKS_IT_MONGO</c> pointing at one.
/// </para>
/// </remarks>
public sealed class PaymentWorkQueueIntegrationTests : IClassFixture<MongoIntegrationFixture>
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));

    private readonly string _tenantId = MongoIntegrationFixture.NewTenantId();

    public PaymentWorkQueueIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;

        // Emptied before each test, because a claim is deliberately tenant-agnostic: it takes the
        // most overdue item in the collection whoever it belongs to, which is the whole point in
        // production and cross-test contamination here. The class fixture shares one database
        // across every test in this file, so without this a test that expects to claim nothing
        // claims whatever the previous test left pending.
        Collection().DeleteMany(FilterDefinition<PaymentBackgroundWork>.Empty);

        Queue().EnsureIndexesAsync(default).GetAwaiter().GetResult();
    }

    private IMongoCollection<PaymentBackgroundWork> Collection() =>
        _fixture.Collection<PaymentBackgroundWork>("PaymentBackgroundWork");

    [Fact]
    public async Task Scheduling_the_same_occurrence_twice_creates_one_item()
    {
        var queue = Queue();

        var first = await queue.ScheduleAsync(Work(), default);
        var second = await queue.ScheduleAsync(Work(), default);

        first.Should().BeTrue();
        // Not an error, and not a second item: a producer that runs twice must not create a second
        // chance to charge the same money.
        second.Should().BeFalse();
    }

    [Fact]
    public async Task Two_workers_racing_for_one_item_do_not_both_get_it()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(), default);

        // Claimed concurrently rather than in sequence: sequential calls would pass even if the
        // claim were a read followed by a write, which is the bug this is here to exclude.
        var races = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(index =>
                queue.ClaimDueAsync(
                    $"lease-{index}",
                    $"worker-{index}",
                    1,
                    TimeSpan.FromMinutes(2),
                    default)));

        races.SelectMany(claimed => claimed).Should().HaveCount(1);
    }

    [Fact]
    public async Task An_item_already_claimed_is_not_claimed_again_while_its_lease_holds()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(), default);

        var first = await queue.ClaimDueAsync(
            "lease-1", "worker-1", 5, TimeSpan.FromMinutes(2), default);
        var second = await queue.ClaimDueAsync(
            "lease-2", "worker-2", 5, TimeSpan.FromMinutes(2), default);

        first.Should().ContainSingle();
        second.Should().BeEmpty();
    }

    [Fact]
    public async Task An_expired_lease_is_reclaimable_by_another_worker()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(), default);

        await queue.ClaimDueAsync("lease-1", "worker-1", 5, TimeSpan.FromMinutes(2), default);

        // The worker holding it died. The lease, not the status, is what says whether anyone is
        // still on the item.
        _time.Advance(TimeSpan.FromMinutes(3));

        var reclaimed = await queue.ClaimDueAsync(
            "lease-2", "worker-2", 5, TimeSpan.FromMinutes(2), default);

        reclaimed.Should().ContainSingle();
        reclaimed[0].AttemptCount.Should().Be(2, "the reclaim is a second attempt, and counts");
    }

    [Fact]
    public async Task Work_that_is_not_due_yet_is_left_alone()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(dueAtUtc: _time.GetUtcNow().UtcDateTime.AddHours(1)), default);

        var claimed = await queue.ClaimDueAsync(
            "lease-1", "worker-1", 5, TimeSpan.FromMinutes(2), default);

        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task Higher_priority_work_is_claimed_first()
    {
        var queue = Queue();
        var earlier = _time.GetUtcNow().UtcDateTime.AddMinutes(-30);

        // The bookkeeping is older; recovering a payment matters more. Ordered by age alone, a
        // backlog of outbox events would delay money.
        await queue.ScheduleAsync(
            Work(workType: PaymentWorkType.StoredPaymentCleanup, dueAtUtc: earlier, priority: 40),
            default);
        await queue.ScheduleAsync(
            Work(
                workType: PaymentWorkType.PaymentReconciliation,
                workKey: "sweep:second",
                priority: 10),
            default);

        var claimed = await queue.ClaimDueAsync(
            "lease-1", "worker-1", 1, TimeSpan.FromMinutes(2), default);

        claimed.Should().ContainSingle();
        claimed[0].WorkType.Should().Be(PaymentWorkType.PaymentReconciliation);
    }

    [Fact]
    public async Task Completion_marks_the_item_and_sets_only_then_when_it_may_be_purged()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(), default);

        var claimed = await queue.ClaimDueAsync(
            "lease-1", "worker-1", 1, TimeSpan.FromMinutes(2), default);

        var completed = await queue.CompleteAsync(
            claimed[0].ItemId, "lease-1", TimeSpan.FromDays(14), default);

        completed.Should().BeTrue();

        var stored = await Stored(claimed[0].ItemId);
        stored.Status.Should().Be(BackgroundWorkStatus.Completed);
        // The TTL index only removes documents that have this, which is what keeps unfinished and
        // dead-lettered work from expiring.
        stored.PurgeAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task An_attempt_whose_lease_moved_on_can_neither_complete_nor_fail_the_item()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(), default);

        var claimed = await queue.ClaimDueAsync(
            "lease-1", "worker-1", 1, TimeSpan.FromMinutes(2), default);

        _time.Advance(TimeSpan.FromMinutes(3));
        await queue.ClaimDueAsync("lease-2", "worker-2", 1, TimeSpan.FromMinutes(2), default);

        // worker-1 comes back from a long provider call to find it no longer speaks for this item.
        var completed = await queue.CompleteAsync(
            claimed[0].ItemId, "lease-1", TimeSpan.FromDays(14), default);

        completed.Should().BeFalse();
        (await Stored(claimed[0].ItemId)).Status.Should().Be(BackgroundWorkStatus.Processing);
    }

    [Fact]
    public async Task A_transient_failure_returns_the_item_with_its_next_attempt_pushed_out()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(), default);

        var claimed = await queue.ClaimDueAsync(
            "lease-1", "worker-1", 1, TimeSpan.FromMinutes(2), default);

        var status = await queue.FailAsync(
            claimed[0].ItemId,
            "lease-1",
            "provider_unreachable",
            "No answer.",
            permanent: false,
            TimeSpan.FromMinutes(5),
            default);

        status.Should().Be(BackgroundWorkStatus.Pending);

        var stored = await Stored(claimed[0].ItemId);
        stored.NextAttemptAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime.AddMinutes(5));
        stored.LeaseId.Should().BeNull("a returned item is nobody's");

        // And it is not claimable until the backoff has passed.
        (await queue.ClaimDueAsync("lease-2", "worker-2", 1, TimeSpan.FromMinutes(2), default))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task A_permanent_failure_dead_letters_immediately()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(), default);

        var claimed = await queue.ClaimDueAsync(
            "lease-1", "worker-1", 1, TimeSpan.FromMinutes(2), default);

        var status = await queue.FailAsync(
            claimed[0].ItemId, "lease-1", "payment_not_found", "Gone.",
            permanent: true, TimeSpan.FromMinutes(5), default);

        status.Should().Be(BackgroundWorkStatus.DeadLetter);
        (await Stored(claimed[0].ItemId)).PurgeAtUtc.Should().BeNull(
            "dead-lettered work is never purged automatically");
    }

    [Fact]
    public async Task Work_runs_out_of_attempts_and_dead_letters_rather_than_retrying_forever()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(maxAttempts: 2), default);

        var status = BackgroundWorkStatus.Pending;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var claimed = await queue.ClaimDueAsync(
                $"lease-{attempt}", "worker-1", 1, TimeSpan.FromMinutes(2), default);

            claimed.Should().ContainSingle($"attempt {attempt} should be claimable");

            status = await queue.FailAsync(
                claimed[0].ItemId, $"lease-{attempt}", "provider_unreachable", "No answer.",
                permanent: false, TimeSpan.Zero, default);
        }

        status.Should().Be(BackgroundWorkStatus.DeadLetter);

        var dead = await queue.ListDeadLetteredAsync(10, default);
        dead.Should().ContainSingle().Which.LastErrorCode.Should().Be("provider_unreachable");
    }

    [Fact]
    public async Task Renewing_a_lease_keeps_an_item_out_of_another_worker_s_reach()
    {
        var queue = Queue();
        await queue.ScheduleAsync(Work(), default);

        var claimed = await queue.ClaimDueAsync(
            "lease-1", "worker-1", 1, TimeSpan.FromMinutes(2), default);

        _time.Advance(TimeSpan.FromMinutes(1));

        var renewed = await queue.RenewLeaseAsync(
            claimed[0].ItemId, "lease-1", TimeSpan.FromMinutes(2), default);

        renewed.Should().BeTrue();

        // Past the original expiry, but not the renewed one: work that outlives its lease says so
        // rather than being taken away mid-provider-call.
        _time.Advance(TimeSpan.FromSeconds(90));

        (await queue.ClaimDueAsync("lease-2", "worker-2", 1, TimeSpan.FromMinutes(2), default))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Depth_reports_what_is_waiting_and_how_long_the_oldest_has_waited()
    {
        var queue = Queue();
        var due = _time.GetUtcNow().UtcDateTime.AddMinutes(-20);

        await queue.ScheduleAsync(Work(dueAtUtc: due), default);
        await queue.ScheduleAsync(Work(workKey: "sweep:second"), default);

        var depths = await queue.DescribeDepthAsync(default);
        var renewal = depths.Single(depth =>
            depth.WorkType == PaymentWorkType.PaymentReconciliation &&
            depth.Status == BackgroundWorkStatus.Pending);

        renewal.Count.Should().Be(2);
        renewal.OldestDueAtUtc.Should().Be(due);
    }

    [Fact]
    public async Task Dead_letters_can_be_listed_for_one_tenant_alone()
    {
        var queue = Queue();
        var otherTenant = MongoIntegrationFixture.NewTenantId();

        foreach (var tenant in new[] { _tenantId, otherTenant })
        {
            var work = Work(maxAttempts: 1);
            work.TenantId = tenant;
            await queue.ScheduleAsync(work, default);

            var claimed = await queue.ClaimDueAsync(
                $"lease-{tenant}", "worker-1", 1, TimeSpan.FromMinutes(2), default);
            await queue.FailAsync(
                claimed[0].ItemId, $"lease-{tenant}", "provider_unreachable", "No answer.",
                permanent: true, TimeSpan.Zero, default);
        }

        (await queue.ListDeadLetteredAsync(10, default)).Should().HaveCount(2);

        // One tenant's operator must not read another tenant's failures, which name their
        // payments and their error codes.
        var mine = await queue.ListDeadLetteredAsync(10, default, _tenantId);
        mine.Should().ContainSingle().Which.TenantId.Should().Be(_tenantId);
    }

    // The subscription suite's three operator tests have no counterpart here: this queue has no
    // TryRequeueAsync or TryAbandonAsync, because the payment module has no audit trail to record
    // such a decision against and so no recovery endpoints were built on it. A dead-lettered
    // payment recovery is visible and stuck, and moving it means editing the collection by hand.
    // See Scheduling/README.md.

    private async Task<PaymentBackgroundWork> Stored(string itemId) =>
        await Collection()
            .Find(stored => stored.ItemId == itemId)
            .FirstAsync();

    private PaymentBackgroundWork Work(
        PaymentWorkType workType = PaymentWorkType.PaymentReconciliation,
        string workKey = "sweep:20260823T1200Z",
        DateTime? dueAtUtc = null,
        int priority = 30,
        int maxAttempts = 5) => new()
    {
        TenantId = _tenantId,
        WorkType = workType,
        WorkKey = workKey,
        DueAtUtc = dueAtUtc ?? _time.GetUtcNow().UtcDateTime,
        NextAttemptAtUtc = dueAtUtc ?? _time.GetUtcNow().UtcDateTime,
        Priority = priority,
        MaxAttempts = maxAttempts,
        CorrelationId = "corr-1"
    };

    private PaymentWorkQueue Queue()
    {
        var secret = new Mock<IBlocksSecret>();
        secret.SetupGet(value => value.DatabaseConnectionString)
            .Returns(MongoIntegrationFixture.ConnectionString);
        secret.SetupGet(value => value.RootDatabaseName).Returns(_fixture.DatabaseName);

        return new PaymentWorkQueue(_fixture.DbContextProvider, secret.Object, _time);
    }
}
