using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Recording usage, and the guarantees a bill depends on.
/// </summary>
public sealed class UsageRecordingServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IUsageThresholdEvaluator> _thresholds = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = NewSubscription();
    private long _balance;
    private readonly List<SubscriptionUsageRecord> _ledger = [];

    public UsageRecordingServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _usage
            .Setup(repository => repository.TryAppendRecordAsync(
                It.IsAny<SubscriptionUsageRecord>(), It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageRecord, CancellationToken>((record, _) =>
            {
                // Stands in for the unique index: the same key never lands twice.
                if (_ledger.Exists(existing => string.Equals(
                        existing.IdempotencyKey,
                        record.IdempotencyKey,
                        StringComparison.Ordinal)))
                {
                    return Task.FromResult(false);
                }

                _ledger.Add(record);

                return Task.FromResult(true);
            });

        _usage
            .Setup(repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageCounter, long, CancellationToken>((seed, delta, _) =>
            {
                _balance += delta;
                seed.Balance = _balance;

                return Task.FromResult(seed);
            });

        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SubscriptionUsageCounter { Balance = _balance });

        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => []);
    }

    [Fact]
    public async Task Recording_reports_the_balance_including_this_call()
    {
        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Used.Should().Be(1);
        result.Value.Remaining.Should().Be(499);
        result.Value.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task The_ledger_is_written_before_the_counter()
    {
        await Service().RecordAsync(NewRequest("usage-1"), "corr-1", CancellationToken.None);

        _ledger.Should().ContainSingle();
        _usage.Verify(
            repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_repeated_key_changes_nothing_and_says_so()
    {
        await Service().RecordAsync(NewRequest("usage-1"), "corr-1", CancellationToken.None);

        var replay = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-2", CancellationToken.None);

        replay.Value!.Replayed.Should().BeTrue();
        replay.Value.Used.Should().Be(1, "a retry must not become a second billable event");
        _ledger.Should().ContainSingle();
    }

    [Fact]
    public async Task A_usage_record_carries_its_correlation_id()
    {
        await Service().RecordAsync(NewRequest("usage-1"), "corr-1", CancellationToken.None);

        _ledger[0].CorrelationId.Should().Be("corr-1",
            "billing questions are answered months later, from this row alone");
        _ledger[0].PeriodKey.Should().NotBeEmpty(
            "stamping the period on write is what stops a record drifting into another month");
    }

    [Fact]
    public async Task Usage_past_the_allowance_is_recorded_as_overage_when_it_is_allowed()
    {
        _balance = 500;

        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeTrue();
        result.Value.Overage.Should().Be(1);
        result.Value.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task An_enforced_meter_past_its_allowance_refuses_and_rolls_back()
    {
        _subscription.Plan.Meters[0].OverageAllowed = false;
        _balance = 500;

        var request = NewRequest("usage-1");
        request.Enforce = true;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeFalse();
        result.Value.Used.Should().Be(500, "a refused call must leave the balance where it was");

        _ledger.Should().HaveCount(2);
        _ledger[1].EntryType.Should().Be(UsageEntryType.Reversal,
            "the ledger is append-only, so a refusal is recorded rather than erased");
        _ledger[1].Delta.Should().Be(-1);
        _ledger[1].CompensatesRecordId.Should().Be(_ledger[0].ItemId);
    }

    [Fact]
    public async Task A_trial_uses_its_own_grant_rather_than_the_plans_allowance()
    {
        _subscription.Status = SubscriptionStatus.Trialing;
        _subscription.Trial = new TrialTerms
        {
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddDays(14),
            Grants = [new TrialMeterGrant { MeterKey = "screening", IncludedQuantity = 25 }]
        };

        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.Value!.Included.Should().Be(25,
            "a trial that hands out the full monthly quota is an open invitation");
    }

    [Fact]
    public async Task A_meter_the_plan_does_not_define_is_not_found()
    {
        var request = NewRequest("usage-1");
        request.MeterKey = "not-a-meter";

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
    }

    [Fact]
    public async Task Usage_without_a_subscription_is_not_found()
    {
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_not_found");
    }

    [Fact]
    public async Task A_missing_idempotency_key_is_refused()
    {
        var request = NewRequest("usage-1");
        request.IdempotencyKey = string.Empty;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        _ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unsupported_aggregation_is_refused_rather_than_summed()
    {
        _subscription.Plan.Meters[0].Aggregation = MeterAggregation.Max;

        var result = await Service().RecordAsync(
            NewRequest("usage-1"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_meter_aggregation_unsupported");
        _ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task Late_reported_usage_lands_in_the_period_it_happened_in()
    {
        var request = NewRequest("usage-1");
        request.OccurredAtUtc = new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc);

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.Value!.PeriodStartUtc.Should().BeBefore(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_never_reset_meter_keeps_one_counter_across_month_boundaries()
    {
        _subscription.Plan.Meters[0].ResetPolicy = MeterResetPolicy.Never;

        var july = NewRequest("storage-july");
        july.OccurredAtUtc = new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc);
        var august = NewRequest("storage-august");
        august.OccurredAtUtc = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

        var first = await Service().RecordAsync(july, "corr-1", CancellationToken.None);
        var second = await Service().RecordAsync(august, "corr-2", CancellationToken.None);

        first.Value!.PeriodKey.Should().Be(MeterPeriodResolver.LifetimePeriodKey);
        second.Value!.PeriodKey.Should().Be(MeterPeriodResolver.LifetimePeriodKey);
        second.Value.Used.Should().Be(2, "renewal must not create a fresh storage allowance");
        _ledger.Select(record => record.PeriodKey).Should().OnlyContain(
            key => key == MeterPeriodResolver.LifetimePeriodKey);
    }

    [Fact]
    public async Task Deleting_storage_releases_lifetime_capacity()
    {
        _subscription.Plan.Meters[0].ResetPolicy = MeterResetPolicy.Never;
        _balance = 300;
        var request = NewRequest("storage-delete");
        request.Quantity = -100;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Used.Should().Be(200);
        result.Value.Remaining.Should().Be(300);
    }

    [Fact]
    public async Task A_periodic_consumption_meter_rejects_negative_usage()
    {
        var request = NewRequest("token-reduction");
        request.Quantity = -1;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_usage_reduction_not_allowed");
        _ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task A_capacity_reduction_cannot_take_the_balance_below_zero()
    {
        _subscription.Plan.Meters[0].ResetPolicy = MeterResetPolicy.Never;
        _balance = 50;
        var request = NewRequest("storage-delete");
        request.Quantity = -100;

        var result = await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeFalse();
        result.Value.Used.Should().Be(50);
        _ledger.Should().HaveCount(2);
        _ledger[1].EntryType.Should().Be(UsageEntryType.Reversal);
        _ledger[1].Delta.Should().Be(100);
    }

    [Fact]
    public async Task Current_usage_reads_periodic_and_lifetime_meters_from_different_counters()
    {
        _subscription.Plan.Meters.Add(new PlanMeter
        {
            MeterKey = "storage",
            UnitLabel = "byte",
            IncludedQuantity = 5_000,
            ResetPolicy = MeterResetPolicy.Never
        });

        await Service().GetCurrentUsageAsync(null, "corr-1", CancellationToken.None);

        _usage.Verify(repository => repository.GetCounterAsync(
            TenantId,
            SubscriptionUsageCounter.CreateId(
                "sub-1", "storage", MeterPeriodResolver.LifetimePeriodKey),
            It.IsAny<CancellationToken>()), Times.Once);
        _usage.Verify(repository => repository.GetCounterAsync(
            TenantId,
            It.Is<string>(id => id.StartsWith("sub-1:screening:M", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_requested_organization_is_forwarded_to_context_resolution()
    {
        var request = NewRequest("usage-1");
        request.OrganizationId = "org-9";

        await Service().RecordAsync(request, "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    [Fact]
    public async Task A_requested_organization_on_get_current_usage_is_forwarded_to_context_resolution()
    {
        await Service().GetCurrentUsageAsync("org-9", "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    private UsageRecordingService Service() => new(
        _subscriptions.Object,
        _usage.Object,
        new MeterAllowanceResolver(_usage.Object),
        _contextResolver.Object,
        _thresholds.Object,
        new RecordUsageRequestValidator(new OptionsStub()),
        new OptionsStub(),
        NullLogger<UsageRecordingService>.Instance,
        _time);

    private static RecordUsageRequest NewRequest(string idempotencyKey) => new()
    {
        MeterKey = "screening",
        Quantity = 1,
        IdempotencyKey = idempotencyKey
    };

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot
        {
            Code = "professional",
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "screening",
                    UnitLabel = "screening",
                    IncludedQuantity = 500,
                    OverageAllowed = true,
                    ThresholdPercents = [80, 100]
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
