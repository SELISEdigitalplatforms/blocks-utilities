using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// The pace a plan is sold at, as distinct from the amount.
/// </summary>
/// <remarks>
/// Ten million tokens a month with nothing shorter can be spent in an afternoon, and a plan priced
/// on the assumption they would not be has no way to say so. A sub-limit is that second window.
/// <para>
/// Guards three ways of getting it wrong. A cap that reports but tells the caller nothing has
/// capped nothing, because this module cannot slow anybody down — the consumer can, on being told.
/// A refusal that leaves the short window holding the refused use opens the next attempt already
/// spent, so somebody who waited exactly as instructed is refused again. And counting both windows
/// into one document would make the cap the period's own balance under another name.
/// </para>
/// </remarks>
public sealed class UsageSubLimitTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string MeterKey = "ai_tokens";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<IUsagePeriodClosureRepository> _closures = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IUsageThresholdEvaluator> _thresholds = new();
    private readonly Mock<IUsageProjectionPublisher> _projection = new();
    private readonly Mock<ISubscriptionUsageCurrentRepository> _current = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    // One balance per counter identity, which is the point: the period window and the short one are
    // different documents, and a harness sharing a number between them could not tell a working
    // sub-limit from a broken one.
    private readonly Dictionary<string, decimal> _balances = new(StringComparer.Ordinal);
    private readonly List<SubscriptionUsageRecord> _ledger = [];

    private SubscriptionDetail _subscription = Metered(
        UsageWindow.Hour, cap: 10, MeterSubLimitBehaviour.Refuse);

    public UsageSubLimitTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _closures
            .Setup(repository => repository.TryAcquireClaimAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageClaimOutcome.Acquired);

        _usage
            .Setup(repository => repository.TryAppendRecordAsync(
                It.IsAny<SubscriptionUsageRecord>(), It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageRecord, CancellationToken>((record, _) =>
            {
                if (_ledger.Exists(existing => string.Equals(
                        existing.IdempotencyKey, record.IdempotencyKey, StringComparison.Ordinal)))
                {
                    return Task.FromResult(false);
                }

                _ledger.Add(record);

                return Task.FromResult(true);
            });

        _usage
            .Setup(repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageCounter, decimal, CancellationToken>((seed, delta, _) =>
            {
                _balances.TryGetValue(seed.ItemId, out var balance);
                balance += delta;
                _balances[seed.ItemId] = balance;
                seed.Balance = balance;

                return Task.FromResult(seed);
            });
    }

    [Fact]
    public async Task Usage_within_the_pace_is_accepted_and_says_nothing()
    {
        var result = await Record(quantity: 4);

        result.Value!.Allowed.Should().BeTrue();
        result.Value.SubLimitExceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Going_past_the_pace_is_refused_when_the_plan_says_so()
    {
        await Record(quantity: 9, key: "first");

        (await Record(quantity: 4, key: "second")).Value!.Allowed
            .Should().BeFalse(
                because: "the plan sells ten an hour, and allowing thirteen would make the cap a " +
                         "suggestion");
    }

    [Fact]
    public async Task A_refused_use_leaves_the_pace_where_it_was()
    {
        await Record(quantity: 9, key: "first");
        await Record(quantity: 4, key: "second");

        (await Record(quantity: 1, key: "third")).Value!.Allowed
            .Should().BeTrue(
                because: "a refused use left counted would open the next attempt already spent, " +
                         "refusing somebody for usage they never made");
    }

    [Fact]
    public async Task Going_past_the_pace_is_allowed_and_reported_when_the_plan_says_so()
    {
        _subscription = Metered(UsageWindow.Hour, cap: 10, MeterSubLimitBehaviour.Throttle);

        await Record(quantity: 9, key: "first");

        var result = await Record(quantity: 4, key: "second");

        result.Value!.Allowed.Should().BeTrue();
        result.Value.SubLimitExceeded.Should().BeTrue(
            because: "nothing here can slow a caller down, so throttling only happens if the " +
                     "caller is told it has gone past the pace");
    }

    [Fact]
    public async Task A_meter_with_no_pace_is_unaffected()
    {
        _subscription = Metered(window: null, cap: null, MeterSubLimitBehaviour.Refuse);

        var result = await Record(quantity: 50);

        result.Value!.Allowed.Should().BeTrue(
            because: "every meter in production has no sub-limit, and they go on capping by " +
                     "period alone");
        result.Value.SubLimitExceeded.Should().BeFalse();
    }

    [Fact]
    public async Task The_pace_is_counted_apart_from_the_period()
    {
        await Record(quantity: 4);

        _balances.Should().HaveCount(2,
            because: "one counter for both would make the cap the period balance under another " +
                     "name, enforcing nothing");
    }

    private async Task<SubscriptionOperationResult<UsageResponse>> Record(
        decimal quantity,
        string key = "idem-1") =>
        await Service().RecordAsync(
            new RecordUsageRequest
            {
                MeterKey = MeterKey,
                Quantity = quantity,
                IdempotencyKey = key,
                Enforce = true
            },
            "corr-1",
            CancellationToken.None);

    private UsageRecordingService Service() => new(
        _subscriptions.Object,
        _usage.Object,
        _closures.Object,
        new MeterAllowanceResolver(_usage.Object),
        _contextResolver.Object,
        _thresholds.Object,
        _projection.Object,
        _current.Object,
        _scheduler.Object,
        new RecordUsageRequestValidator(new OptionsStub()),
        new OptionsStub(),
        NullLogger<UsageRecordingService>.Instance,
        _time);

    private static SubscriptionDetail Metered(
        UsageWindow? window,
        decimal? cap,
        MeterSubLimitBehaviour behaviour) => new()
        {
            ItemId = "sub-1",
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            Status = SubscriptionStatus.Active,
            CurrencyCode = "CHF",
            CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            UsageSchedule = new BillingSchedule
            {
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                AnchorInstantUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                AnchorDayOfMonth = 1
            },
            Plan = new PlanSnapshot
            {
                Code = "starter",
                Meters =
                [
                    new PlanMeter
                    {
                        MeterKey = MeterKey,
                        UnitLabel = "token",
                        // Generous deliberately: every refusal here has to come from the pace,
                        // never from running out for the month.
                        IncludedQuantity = 1_000,
                        OverageAllowed = false,
                        SubLimitWindow = window,
                        SubLimitQuantity = cap,
                        SubLimitBehaviour = behaviour
                    }
                ]
            }
        };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
