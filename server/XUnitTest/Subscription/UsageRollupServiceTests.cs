using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Folding the append-only usage ledger into the tenant-wide activity and actor rollups.
/// </summary>
public sealed class UsageRollupServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";

    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionUsageActivityRollupRepository> _activity = new();
    private readonly Mock<ISubscriptionUsageActorRollupRepository> _actors = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionDocumentCursorRepository> _cursors = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));

    private IReadOnlyList<SubscriptionUsageRecord> _records = [];
    private FinancialDocumentSweepMark? _cursorMark;

    public UsageRollupServiceTests()
    {
        _usage
            .Setup(repository => repository.ListRecordedSinceAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _records);

        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, string subscriptionId, CancellationToken _) =>
                NewSubscription(subscriptionId));

        _cursors
            .Setup(repository => repository.GetAsync(
                TenantId, UsageRollupService.RollupCursorName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _cursorMark);

        _cursors
            .Setup(repository => repository.SetAsync(
                TenantId, UsageRollupService.RollupCursorName,
                It.IsAny<FinancialDocumentSweepMark>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, FinancialDocumentSweepMark, CancellationToken>(
                (_, _, mark, _) => _cursorMark = mark)
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task A_batch_applies_each_record_to_the_activity_rollup_and_advances_the_cursor()
    {
        var first = NewRecord(
            "rec-1", "screening",
            occurredAtUtc: new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            recordedAtUtc: new DateTime(2026, 9, 1, 8, 0, 5, DateTimeKind.Utc),
            delta: 3);
        var second = NewRecord(
            "rec-2", "envelope",
            occurredAtUtc: new DateTime(2026, 9, 2, 14, 0, 0, DateTimeKind.Utc),
            recordedAtUtc: new DateTime(2026, 9, 2, 14, 0, 5, DateTimeKind.Utc),
            delta: 5);
        _records = [first, second];

        var processed = await Service().RunBatchAsync(TenantId, "corr-1", CancellationToken.None);

        processed.Should().Be(2);

        _activity.Verify(repository => repository.ApplyAsync(
            TenantId, OrganizationId, SubscriptionId, "screening",
            It.IsAny<string>(), It.IsAny<string>(),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 8, 3m,
            first.RecordedAtUtc, "rec-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _activity.Verify(repository => repository.ApplyAsync(
            TenantId, OrganizationId, SubscriptionId, "envelope",
            It.IsAny<string>(), It.IsAny<string>(),
            new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), 14, 5m,
            second.RecordedAtUtc, "rec-2", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _cursors.Verify(repository => repository.SetAsync(
            TenantId, UsageRollupService.RollupCursorName,
            new FinancialDocumentSweepMark(second.RecordedAtUtc, "rec-2"),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "the cursor advances to the last record's RecordedAtUtc and id, not the first's");
    }

    /// <summary>
    /// A record reported late — recorded well after it actually occurred — must still be folded
    /// into the bucket for the day it happened, not the day the rollup job caught up to it. The
    /// cursor itself still advances on RecordedAtUtc; only the bucket key uses OccurredAtUtc.
    /// </summary>
    [Fact]
    public async Task A_late_arriving_record_is_bucketed_by_the_day_it_occurred_not_the_day_it_was_recorded()
    {
        var lateRecord = NewRecord(
            "rec-late", "screening",
            occurredAtUtc: new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc),
            recordedAtUtc: new DateTime(2026, 9, 4, 9, 0, 0, DateTimeKind.Utc),
            delta: 7);
        _records = [lateRecord];

        await Service().RunBatchAsync(TenantId, "corr-1", CancellationToken.None);

        _activity.Verify(repository => repository.ApplyAsync(
            TenantId, OrganizationId, SubscriptionId, "screening",
            It.IsAny<string>(), It.IsAny<string>(),
            new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), // the day it occurred.
            3, 7m,
            lateRecord.RecordedAtUtc, "rec-late", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "a late-arriving record must land in the day it occurred, not the day it was recorded");

        _activity.Verify(repository => repository.ApplyAsync(
            TenantId, OrganizationId, SubscriptionId, "screening",
            It.IsAny<string>(), It.IsAny<string>(),
            new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc), // the day it was recorded.
            It.IsAny<int>(), It.IsAny<decimal>(),
            It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_record_with_an_actor_is_also_applied_to_the_actor_rollup()
    {
        var record = NewRecord(
            "rec-1", "screening",
            occurredAtUtc: new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            recordedAtUtc: new DateTime(2026, 9, 1, 8, 0, 5, DateTimeKind.Utc),
            delta: 3,
            recordedByUserId: "user-1");
        _records = [record];

        await Service().RunBatchAsync(TenantId, "corr-1", CancellationToken.None);

        _actors.Verify(repository => repository.ApplyAsync(
            TenantId, OrganizationId, "screening",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), "user-1", 3m,
            record.RecordedAtUtc, "rec-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_record_with_no_actor_is_not_applied_to_the_actor_rollup()
    {
        var record = NewRecord(
            "rec-1", "screening",
            occurredAtUtc: new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            recordedAtUtc: new DateTime(2026, 9, 1, 8, 0, 5, DateTimeKind.Utc),
            delta: 3,
            recordedByUserId: null);
        _records = [record];

        await Service().RunBatchAsync(TenantId, "corr-1", CancellationToken.None);

        _actors.Verify(repository => repository.ApplyAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a record with no attributed actor must not create an unattributed actor bucket");
    }

    [Fact]
    public async Task Nothing_new_since_the_cursor_returns_zero_and_does_not_advance_it()
    {
        _records = [];

        var processed = await Service().RunBatchAsync(TenantId, "corr-1", CancellationToken.None);

        processed.Should().Be(0);
        _cursors.Verify(repository => repository.SetAsync(
            TenantId, It.IsAny<string>(), It.IsAny<FinancialDocumentSweepMark>(),
            It.IsAny<CancellationToken>()),
            Times.Never,
            "a re-run that finds nothing new must not touch the cursor");
        _activity.Verify(repository => repository.ApplyAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(),
            It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static SubscriptionUsageRecord NewRecord(
        string itemId,
        string meterKey,
        DateTime occurredAtUtc,
        DateTime recordedAtUtc,
        decimal delta,
        string? recordedByUserId = "user-1") => new()
    {
        ItemId = itemId,
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        SubscriptionId = SubscriptionId,
        MeterKey = meterKey,
        PeriodKey = "M20260901T000000Z",
        EntryType = UsageEntryType.Consumption,
        Delta = delta,
        IdempotencyKey = itemId,
        OccurredAtUtc = occurredAtUtc,
        RecordedAtUtc = recordedAtUtc,
        RecordedByUserId = recordedByUserId,
        CorrelationId = "corr-1"
    };

    private static SubscriptionDetail NewSubscription(string id) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot
        {
            PlanId = "plan-1",
            Code = "professional",
            Meters =
            [
                new PlanMeter { MeterKey = "screening", IncludedQuantity = 500 },
                new PlanMeter { MeterKey = "envelope", IncludedQuantity = 100 }
            ]
        }
    };

    private UsageRollupService Service() => new(
        _usage.Object,
        _activity.Object,
        _actors.Object,
        _subscriptions.Object,
        _cursors.Object,
        new OptionsStub(),
        NullLogger<UsageRollupService>.Instance,
        _time);

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
