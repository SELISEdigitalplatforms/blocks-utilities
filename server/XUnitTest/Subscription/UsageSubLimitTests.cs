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
/// <para>
/// A rolling pace is the same guarantee measured a different way: the span ends now rather than on
/// the clock, so what counts as "within the window" changes on every call rather than only at a
/// boundary. Its own tests guard the case a fixed window cannot even express — a burst spread just
/// wide of one clock-aligned hour that a rolling five-hour rule still has to catch.
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
    // Rolling counters, keyed the same way the real repository would key a document: one per
    // rule, holding every bucket the rule has ever written.
    private readonly Dictionary<string, SubscriptionUsageCounter> _buckets =
        new(StringComparer.Ordinal);
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

        _usage
            .Setup(repository => repository.ApplyBucketDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<string>(), It.IsAny<decimal>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageCounter, string, decimal, IReadOnlyCollection<string>,
                CancellationToken>((seed, bucket, delta, expired, _) =>
            {
                if (!_buckets.TryGetValue(seed.ItemId, out var counter))
                {
                    counter = new SubscriptionUsageCounter
                    {
                        ItemId = seed.ItemId,
                        Buckets = new Dictionary<string, decimal>(StringComparer.Ordinal)
                    };
                    _buckets[seed.ItemId] = counter;
                }

                counter.Buckets ??= new Dictionary<string, decimal>(StringComparer.Ordinal);
                counter.Buckets.TryGetValue(bucket, out var existing);
                counter.Buckets[bucket] = existing + delta;

                foreach (var stale in expired)
                {
                    counter.Buckets.Remove(stale);
                }

                counter.AppliedRecordCount++;

                return Task.FromResult(counter);
            });

        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((_, itemId, _) =>
                Task.FromResult(_buckets.TryGetValue(itemId, out var counter) ? counter : null));
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

    [Fact]
    public async Task A_rolling_pace_refuses_a_burst_that_a_fixed_hour_would_have_allowed()
    {
        // Nine at 09:59, four at 10:01 — two different clock-aligned hours, five hours apart
        // is one rolling window and thirteen inside it.
        _subscription = RollingMetered(UsageWindow.Hour, count: 5, cap: 10);

        await RecordAt(new DateTimeOffset(2026, 8, 14, 9, 59, 0, TimeSpan.Zero), 9, "first");

        (await RecordAt(new DateTimeOffset(2026, 8, 14, 10, 1, 0, TimeSpan.Zero), 4, "second"))
            .Value!.Allowed
            .Should().BeFalse(
                because: "the two uses are four minutes apart and well inside a five-hour span, " +
                         "which is exactly the burst a rolling rule exists to catch and a " +
                         "clock-aligned hour would have let through as two separate windows");
    }

    [Fact]
    public async Task Usage_that_falls_out_of_the_rolling_span_stops_counting_against_it()
    {
        _subscription = RollingMetered(UsageWindow.Hour, count: 5, cap: 10);

        await RecordAt(new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero), 9, "first");

        // Five hours and one minute later, the first use has aged out of the span.
        (await RecordAt(new DateTimeOffset(2026, 8, 14, 14, 1, 0, TimeSpan.Zero), 9, "second"))
            .Value!.Allowed
            .Should().BeTrue(
                because: "the rule looks back five hours from now, and usage older than that is " +
                         "not within the window whatever it once counted toward");
    }

    [Fact]
    public async Task A_refused_rolling_use_leaves_the_span_where_it_was()
    {
        _subscription = RollingMetered(UsageWindow.Hour, count: 5, cap: 10);

        var now = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

        await RecordAt(now, 9, "first");
        await RecordAt(now, 4, "second");

        (await RecordAt(now, 1, "third")).Value!.Allowed
            .Should().BeTrue(
                because: "a refused use counted against the span would open the next attempt " +
                         "already spent, refusing somebody for usage they were never allowed to " +
                         "make");
    }

    [Fact]
    public async Task The_plans_own_count_is_what_sets_the_span_not_a_single_window()
    {
        // Two hours apart. A one-hour span has let the first use age out by the second; a
        // five-hour span has not — so the count on the plan is the only thing distinguishing
        // "allowed" from "refused" here.
        _subscription = RollingMetered(UsageWindow.Hour, count: 5, cap: 10);

        var first = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

        await RecordAt(first, 9, "first");

        (await RecordAt(first.AddHours(2), 4, "second")).Value!.Allowed
            .Should().BeFalse(
                because: "five hours is what the plan was authored with, and two hours apart is " +
                         "well inside that span — a rule that quietly measured one hour instead " +
                         "would allow this and enforce a limit the plan never sold");
    }

    [Fact]
    public async Task A_rolling_pace_can_be_allowed_and_reported_like_a_fixed_one()
    {
        _subscription = RollingMetered(
            UsageWindow.Hour, count: 5, cap: 10, MeterSubLimitBehaviour.Throttle);

        var now = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

        await RecordAt(now, 9, "first");

        var result = await RecordAt(now, 4, "second");

        result.Value!.Allowed.Should().BeTrue();
        result.Value.SubLimitExceeded.Should().BeTrue(
            because: "a rolling rule reports over-pace the same way a fixed one does — nothing " +
                     "here can slow a caller down by itself");
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

    [Fact]
    public async Task Buckets_well_outside_the_span_are_pruned_rather_than_kept_forever()
    {
        _subscription = RollingMetered(UsageWindow.Hour, count: 1, cap: 10);

        var first = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

        await RecordAt(first, 1, "first");

        // Three hours later — well past even the two-span grace an expiring bucket is kept for.
        await RecordAt(first.AddHours(3), 1, "second");

        _buckets.Should().ContainSingle().Which.Value.Buckets.Should().ContainSingle(
            because: "a counter that keeps every bucket it has ever written grows without bound, " +
                     "and a rule running for months would carry every minute of that history");
    }

    private async Task<SubscriptionOperationResult<UsageResponse>> RecordAt(
        DateTimeOffset now,
        decimal quantity,
        string key)
    {
        _time.Advance(now - _time.GetUtcNow());

        return await Record(quantity, key);
    }

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

    private static SubscriptionDetail RollingMetered(
        UsageWindow window,
        int count,
        decimal cap,
        MeterSubLimitBehaviour behaviour = MeterSubLimitBehaviour.Refuse)
    {
        var subscription = Metered(window, cap, behaviour);

        subscription.Plan.Meters[0].SubLimitWindowCount = count;
        subscription.Plan.Meters[0].SubLimitRolling = true;

        return subscription;
    }

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
