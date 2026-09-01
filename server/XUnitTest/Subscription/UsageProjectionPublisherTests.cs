using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Publishing the current-usage projection.
/// </summary>
/// <remarks>
/// The guarantee under test throughout is that the projection is <em>derived</em>: every figure it
/// stores comes from a counter result, and nothing here adds, subtracts or carries anything forward.
/// A projection that did its own arithmetic would disagree with the bill exactly when it mattered.
/// </remarks>
public sealed class UsageProjectionPublisherTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionUsageCurrentRepository> _current = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    private readonly List<SubscriptionUsageCurrent> _published = [];
    private readonly List<SubscriptionUsageCurrent> _seeded = [];

    public UsageProjectionPublisherTests()
    {
        _current
            .Setup(repository => repository.TryPublishAsync(
                It.IsAny<SubscriptionUsageCurrent>(), It.IsAny<CancellationToken>()))
            .Callback((SubscriptionUsageCurrent document, CancellationToken _) =>
                _published.Add(document))
            .ReturnsAsync(true);

        _current
            .Setup(repository => repository.TrySeedAsync(
                It.IsAny<SubscriptionUsageCurrent>(), It.IsAny<CancellationToken>()))
            .Callback((SubscriptionUsageCurrent document, CancellationToken _) =>
                _seeded.Add(document))
            .ReturnsAsync(true);

        _usage
            .Setup(repository => repository.GetCountersAsync(
                TenantId,
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SubscriptionUsageCounter>(StringComparer.Ordinal));

        _usage
            .Setup(repository => repository.SummariseLedgerAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0L, 0L));
    }

    [Fact]
    public async Task It_copies_the_counter_figures_rather_than_recomputing_them()
    {
        var counter = Counter(balance: 120, appliedRecordCount: 7);

        var outcome = await Publisher().PublishAsync(
            Subscription(), Meter(), Period(), counter, allowance: 100, "corr-1",
            CancellationToken.None);

        outcome.Should().Be(UsageProjectionOutcome.Published);

        var document = _published.Should().ContainSingle().Subject;
        document.Used.Should().Be(120, "taken from the counter, not counted here");
        document.Included.Should().Be(100);
        document.Remaining.Should().Be(0, "never negative");
        document.Overage.Should().Be(20);
    }

    /// <summary>
    /// The version is what makes concurrent publishing safe, so it has to be the counter's own
    /// monotonic count and not a timestamp or an attempt number.
    /// </summary>
    [Fact]
    public async Task It_versions_the_document_with_the_counters_applied_record_count()
    {
        await Publisher().PublishAsync(
            Subscription(), Meter(), Period(), Counter(balance: 5, appliedRecordCount: 42),
            allowance: 100, "corr-1", CancellationToken.None);

        _published.Should().ContainSingle().Which.SourceVersion.Should().Be(42);
    }

    [Fact]
    public async Task It_carries_the_scope_plan_and_meter_terms_a_direct_reader_needs()
    {
        await Publisher().PublishAsync(
            Subscription(), Meter(), Period(), Counter(1, 1), 100, "corr-1",
            CancellationToken.None);

        var document = _published.Should().ContainSingle().Subject;
        document.TenantId.Should().Be(TenantId);
        document.OrganizationId.Should().Be(OrganizationId);
        document.SubscriptionId.Should().Be("sub-1");
        document.SubscriptionStatus.Should().Be(SubscriptionStatus.Active);
        document.PlanId.Should().Be("plan-1");
        document.PlanCode.Should().Be("pro");
        document.MeterKey.Should().Be("screening");
        document.UnitLabel.Should().Be("screening");
        document.OverageAllowed.Should().BeTrue();
        document.SchemaVersion.Should().Be(SubscriptionUsageCurrent.CurrentSchemaVersion);
    }

    /// <summary>
    /// Superseded is not a failure. It means a later recording published a newer figure first, so the
    /// projection is ahead of this caller rather than missing, and nothing needs repairing.
    /// </summary>
    [Fact]
    public async Task A_publish_that_loses_the_version_race_is_reported_as_superseded_not_repaired()
    {
        _current
            .Setup(repository => repository.TryPublishAsync(
                It.IsAny<SubscriptionUsageCurrent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var outcome = await Publisher().PublishAsync(
            Subscription(), Meter(), Period(), Counter(1, 1), 100, "corr-1",
            CancellationToken.None);

        outcome.Should().Be(UsageProjectionOutcome.Superseded);
        _scheduler.Verify(
            scheduler => scheduler.ScheduleUsageProjectionRefreshAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The whole reason this is a projection and not an authority: the usage has already committed by
    /// the time it is published, so a failure here must be absorbed and repaired, never thrown.
    /// </summary>
    [Fact]
    public async Task A_failed_publish_schedules_one_repair_and_does_not_throw()
    {
        // A plain failure rather than a transient one, so the retry does not run and this test is
        // about what happens when the write genuinely will not go through.
        _current
            .Setup(repository => repository.TryPublishAsync(
                It.IsAny<SubscriptionUsageCurrent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the projection write failed"));

        var outcome = await Publisher().PublishAsync(
            Subscription(), Meter(), Period(), Counter(1, 1), 100, "corr-1",
            CancellationToken.None);

        outcome.Should().Be(UsageProjectionOutcome.RepairScheduled);
        _scheduler.Verify(
            scheduler => scheduler.ScheduleUsageProjectionRefreshAsync(
                TenantId, OrganizationId, "sub-1", "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Cancellation is the caller going away, not a projection problem. Scheduling a repair for it
    /// would fill the queue with work nobody is waiting for every time a client disconnects.
    /// </summary>
    [Fact]
    public async Task Cancellation_propagates_instead_of_scheduling_a_repair()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        _current
            .Setup(repository => repository.TryPublishAsync(
                It.IsAny<SubscriptionUsageCurrent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await Publisher().PublishAsync(
            Subscription(), Meter(), Period(), Counter(1, 1), 100, "corr-1", cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _scheduler.Verify(
            scheduler => scheduler.ScheduleUsageProjectionRefreshAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// So a consumer can discover a meter and its allowance before anything has been recorded — the
    /// difference, to a reader that cannot see the plan, between "no usage yet" and "no such meter".
    /// </summary>
    [Fact]
    public async Task Seeding_creates_a_zero_usage_document_for_every_current_window()
    {
        var seeded = await Publisher().SeedCurrentAsync(
            Subscription(), _time.GetUtcNow().UtcDateTime, "corr-1", CancellationToken.None);

        seeded.Should().Be(2, "one periodic meter and one lifetime capacity meter");
        _seeded.Should().OnlyContain(document =>
            document.Used == 0 && document.Overage == 0 && document.SourceVersion == 0);
    }

    /// <summary>
    /// The two meters resolve to different period keys, which is the fact the whole batching design
    /// rests on. Asserted here so a change to MeterPeriodResolver cannot quietly invalidate it.
    /// </summary>
    [Fact]
    public async Task Seeding_addresses_a_lifetime_meter_and_a_periodic_meter_separately()
    {
        await Publisher().SeedCurrentAsync(
            Subscription(), _time.GetUtcNow().UtcDateTime, "corr-1", CancellationToken.None);

        _seeded.Should().Contain(document =>
            document.MeterKey == "storage" &&
            document.PeriodKey == MeterPeriodResolver.LifetimePeriodKey);
        _seeded.Should().Contain(document =>
            document.MeterKey == "screening" &&
            document.PeriodKey != MeterPeriodResolver.LifetimePeriodKey);
    }

    /// <summary>
    /// A window with no counter has had nothing recorded in it. Seeded rather than published: a
    /// publish carries version 0, which the version condition would refuse against any existing
    /// document — and would be wrong to accept if it did, because it would overwrite a real balance
    /// with zero.
    /// </summary>
    [Fact]
    public async Task Refreshing_a_window_with_no_counter_seeds_it_rather_than_publishing_zero()
    {
        await Publisher().RefreshAsync(
            Subscription(), _time.GetUtcNow().UtcDateTime, "corr-1", CancellationToken.None);

        _published.Should().BeEmpty();
        _seeded.Should().HaveCount(2).And.OnlyContain(document => document.Used == 0);
    }

    [Fact]
    public async Task Refreshing_publishes_the_counter_state_for_a_window_that_has_one()
    {
        // Keyed off whichever ids the publisher actually composes rather than a period key spelled
        // out here: the schedule decides that format, and hardcoding it would make this test a
        // statement about BillingPeriodCalculator instead of about refreshing.
        _usage
            .Setup(repository => repository.GetCountersAsync(
                TenantId,
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> ids, CancellationToken _) => ids
                .Where(id => id.Contains(":screening:", StringComparison.Ordinal))
                .ToDictionary(
                    id => id,
                    id => Counter(balance: 33, appliedRecordCount: 9, itemId: id),
                    StringComparer.Ordinal));

        await Publisher().RefreshAsync(
            Subscription(), _time.GetUtcNow().UtcDateTime, "corr-1", CancellationToken.None);

        _published.Should().ContainSingle().Which.Used.Should().Be(33);
        _seeded.Should().ContainSingle("the lifetime meter still has no counter");
    }

    /// <summary>
    /// One batch for every meter, on the repair path as much as the read path: a subscription with a
    /// dozen meters would otherwise cost a dozen round trips per repair.
    /// </summary>
    [Fact]
    public async Task Refreshing_reads_every_counter_in_one_batch()
    {
        await Publisher().RefreshAsync(
            Subscription(), _time.GetUtcNow().UtcDateTime, "corr-1", CancellationToken.None);

        _usage.Verify(
            repository => repository.GetCountersAsync(
                TenantId,
                It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _usage.Verify(
            repository => repository.GetCounterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A lifetime capacity meter's window never ends, so its projection must not be given an expiry
    /// its allowance would outlive.
    /// </summary>
    [Fact]
    public async Task A_lifetime_window_is_seeded_without_an_expiry()
    {
        await Publisher().SeedCurrentAsync(
            Subscription(), _time.GetUtcNow().UtcDateTime, "corr-1", CancellationToken.None);

        _seeded
            .Should().ContainSingle(document => document.MeterKey == "storage")
            .Which.ExpiresAtUtc.Should().Be(DateTime.MaxValue);
    }

    /// <summary>The projection must not outlive the counter it projects.</summary>
    [Fact]
    public async Task A_published_document_inherits_the_counters_own_expiry()
    {
        var expires = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var counter = Counter(balance: 1, appliedRecordCount: 1);
        counter.ExpiresAtUtc = expires;

        await Publisher().PublishAsync(
            Subscription(), Meter(), Period(), counter, 100, "corr-1", CancellationToken.None);

        _published.Should().ContainSingle().Which.ExpiresAtUtc.Should().Be(expires);
    }

    private UsageProjectionPublisher Publisher() => new(
        _current.Object,
        _usage.Object,
        new MeterAllowanceResolver(_usage.Object),
        _scheduler.Object,
        new OptionsStub(),
        NullLogger<UsageProjectionPublisher>.Instance,
        _time);

    private static SubscriptionUsageCounter Counter(
        long balance,
        long appliedRecordCount,
        string? itemId = null) => new()
    {
        ItemId = itemId ?? SubscriptionUsageCounter.CreateId("sub-1", "screening", "M2026-09"),
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        SubscriptionId = "sub-1",
        MeterKey = "screening",
        Balance = balance,
        AppliedRecordCount = appliedRecordCount,
        ExpiresAtUtc = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
    };

    private static PlanMeter Meter() => new()
    {
        MeterKey = "screening",
        UnitLabel = "screening",
        IncludedQuantity = 100,
        OverageAllowed = true,
        ResetPolicy = MeterResetPolicy.Periodic
    };

    private static BillingPeriod Period() =>
        new(
            1,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            "M2026-09");

    private static SubscriptionDetail Subscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Plan = new PlanSnapshot
        {
            PlanId = "plan-1",
            Code = "pro",
            Meters =
            [
                Meter(),
                new PlanMeter
                {
                    MeterKey = "storage",
                    UnitLabel = "GB",
                    IncludedQuantity = 500,
                    OverageAllowed = false,
                    ResetPolicy = MeterResetPolicy.Never
                }
            ]
        },
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        }
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
