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

    /// <summary>
    /// A fixed pace spanning several windows caps across the whole block, not one window of it.
    /// </summary>
    /// <remarks>
    /// The regression this guards: the count was authored, validated and shown in the console
    /// while the counter it seeded stayed addressed by a single window regardless. A plan sold as
    /// "10,000 every 6 hours" enforced 10,000 an hour, six times over, and nothing failed —
    /// every existing test exercised either a single-window fixed pace or a rolling one, and the
    /// gap was the combination neither covered.
    /// </remarks>
    [Fact]
    public async Task A_fixed_pace_spanning_several_hours_caps_across_the_whole_block()
    {
        _subscription = Metered(UsageWindow.Hour, cap: 10, MeterSubLimitBehaviour.Refuse);
        _subscription.Plan.Meters[0].SubLimitWindowCount = 6;

        // Both inside the same six-hour block starting at midnight: 02:00 and 05:00.
        await RecordAt(new DateTimeOffset(2026, 8, 14, 2, 0, 0, TimeSpan.Zero), 9, "first");

        (await RecordAt(new DateTimeOffset(2026, 8, 14, 5, 0, 0, TimeSpan.Zero), 4, "second"))
            .Value!.Allowed
            .Should().BeFalse(
                because: "the plan sells ten every six hours, and both uses fall in the block " +
                         "that runs from midnight to 06:00 — a cap that only ever saw one hour " +
                         "at a time would have refused neither");
    }

    [Fact]
    public async Task A_fixed_multi_hour_block_resets_when_the_next_block_starts()
    {
        _subscription = Metered(UsageWindow.Hour, cap: 10, MeterSubLimitBehaviour.Refuse);
        _subscription.Plan.Meters[0].SubLimitWindowCount = 6;

        await RecordAt(new DateTimeOffset(2026, 8, 14, 2, 0, 0, TimeSpan.Zero), 9, "first");

        // 06:00 opens the next six-hour block, so the nine spent in the first is behind it.
        (await RecordAt(new DateTimeOffset(2026, 8, 14, 6, 0, 0, TimeSpan.Zero), 9, "second"))
            .Value!.Allowed
            .Should().BeTrue(
                because: "a block that never reset would eventually refuse every use once, " +
                         "however far apart, which is a fixed pace behaving like a lifetime cap");
    }

    /// <summary>
    /// A count of one is not merely equivalent to the plan every subscriber already has — it has
    /// to be addressed exactly the way it always has been.
    /// </summary>
    [Fact]
    public async Task A_window_count_of_one_addresses_the_same_counter_as_before_counts_existed()
    {
        _subscription = Metered(UsageWindow.Hour, cap: 10, MeterSubLimitBehaviour.Refuse);
        _subscription.Plan.Meters[0].SubLimitWindowCount = 1;

        await Record(quantity: 9, key: "first");

        _balances.Keys.Should().Contain(
            SubscriptionUsageCounter.CreateId("sub-1", MeterKey, "h20260814T120000Z"),
            because: "every counter a production meter has already written was addressed by " +
                     "this three-part identity, and a count of one has to keep reading it");
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

    [Fact]
    public async Task Every_limit_on_a_meter_counts_the_same_use()
    {
        _subscription = Limited(
            Limit(UsageWindow.Hour, 10),
            Limit(UsageWindow.Day, 15));

        var start = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

        (await RecordAt(start, 9, "first")).Value!.Allowed.Should().BeTrue();

        // A new hour, so the hourly limit has room — but the day now holds eighteen of fifteen.
        var second = await RecordAt(start.AddHours(1), 9, "second");

        second.Value!.Allowed.Should().BeFalse(
            because: "the use fits the hour and not the day, and a meter selling both has to " +
                     "hold it to both");
        second.Value.ExceededSubLimits.Should().ContainSingle()
            .Which.Window.Should().Be(nameof(UsageWindow.Day),
                because: "the caller is told which limit stopped them, not only that one did");
    }

    [Fact]
    public async Task A_reporting_limit_past_its_pace_does_not_refuse_while_a_refusing_one_has_room()
    {
        _subscription = Limited(
            Limit(UsageWindow.Hour, 10, MeterSubLimitBehaviour.Throttle),
            Limit(UsageWindow.Week, 1_000));

        await Record(quantity: 9, key: "first");
        var result = await Record(quantity: 4, key: "second");

        result.Value!.Allowed.Should().BeTrue(
            because: "only the hourly pace is past, and the plan asked for it to report, not refuse");
        result.Value.SubLimitExceeded.Should().BeTrue();
        result.Value.ExceededSubLimits.Should().ContainSingle()
            .Which.Window.Should().Be(nameof(UsageWindow.Hour));
    }

    /// <summary>
    /// The limit that refused is rarely the only one counted. Left in any of the others, a use
    /// nobody was allowed to make would open that limit's next window already spent.
    /// </summary>
    [Fact]
    public async Task A_refused_use_is_taken_back_out_of_every_limit_not_only_the_one_that_refused()
    {
        _subscription = Limited(
            Limit(UsageWindow.Hour, 10),
            Limit(UsageWindow.Week, 1_000));

        await Record(quantity: 9, key: "first");
        (await Record(quantity: 4, key: "second")).Value!.Allowed.Should().BeFalse();

        _balances.Where(entry => entry.Key.Contains(":w", StringComparison.Ordinal))
            .Should().ContainSingle()
            .Which.Value.Should().Be(9,
                because: "the week never allowed the refused four, so it must not hold them");
    }

    [Fact]
    public async Task A_meter_stored_with_the_legacy_single_pace_is_enforced_as_one_limit()
    {
        // Metered builds the meter with the single fields, as every plan written before the list
        // was stored.
        _subscription = Metered(UsageWindow.Hour, cap: 10, MeterSubLimitBehaviour.Refuse);

        await Record(quantity: 9, key: "first");

        (await Record(quantity: 4, key: "second")).Value!.Allowed.Should().BeFalse();
    }

    /// <remarks>
    /// The paces used to live only in their own window counters, which a reader of the usage
    /// projection could not reach.
    /// </remarks>
    [Fact]
    public async Task A_recording_publishes_how_much_of_each_pace_is_spent()
    {
        await Record(quantity: 4);

        _projection.Verify(projection => projection.PublishAsync(
            It.IsAny<SubscriptionDetail>(), It.IsAny<PlanMeter>(), It.IsAny<BillingPeriod>(),
            It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<string?>(),
            It.Is<IReadOnlyList<SubscriptionUsageCurrentSubLimit>?>(paces =>
                paces != null && paces.Count == 1 &&
                paces[0].Used == 4 && paces[0].Remaining == 6 && !paces[0].Exceeded &&
                paces[0].WindowEndUtc != null)),
            Times.Once);
    }

    [Fact]
    public async Task A_refused_use_publishes_the_paces_as_they_stand_once_it_is_put_back()
    {
        await Record(quantity: 9, key: "first");
        await Record(quantity: 4, key: "second");

        _projection.Verify(projection => projection.PublishAsync(
            It.IsAny<SubscriptionDetail>(), It.IsAny<PlanMeter>(), It.IsAny<BillingPeriod>(),
            It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<string?>(),
            It.Is<IReadOnlyList<SubscriptionUsageCurrentSubLimit>?>(paces =>
                paces != null && paces[0].Used == 9)),
            Times.Exactly(2),
            "both the first use and the refusal leave nine spent; the refused four never counted");
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

    private static PlanMeterSubLimit Limit(
        UsageWindow window,
        decimal quantity,
        MeterSubLimitBehaviour behaviour = MeterSubLimitBehaviour.Refuse) =>
        new() { Window = window, Quantity = quantity, Behaviour = behaviour };

    private static SubscriptionDetail Limited(params PlanMeterSubLimit[] limits)
    {
        var subscription = Metered(window: null, cap: null, MeterSubLimitBehaviour.Refuse);

        subscription.Plan.Meters[0].SubLimits = [.. limits];

        return subscription;
    }

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
