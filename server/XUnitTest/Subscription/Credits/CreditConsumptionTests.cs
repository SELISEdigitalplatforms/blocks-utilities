using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription.Credits;

/// <summary>
/// Spending bought credits at the usage gate (spec 002, phase C).
/// </summary>
/// <remarks>
/// Guards against the two ways credits cost a subscriber twice: usage they paid for with credits
/// being billed again as overage at period close, and credits spent on a recording that was then
/// refused. Also against spending them while the plan's own allowance — which is lost at the end
/// of the window — still has room.
/// </remarks>
public sealed class CreditConsumptionTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<IUsagePeriodClosureRepository> _closures = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IUsageThresholdEvaluator> _thresholds = new();
    private readonly Mock<IUsageProjectionPublisher> _projection = new();
    private readonly Mock<ISubscriptionUsageCurrentRepository> _current = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly Mock<ISubscriptionOutboxEventFactory> _events = new();
    private readonly InMemoryCreditGrants _grants = new();
    private readonly List<string> _published = [];

    private readonly SubscriptionDetail _subscription = Subscription();
    private SubscriptionUsageCounter? _counter;
    private IReadOnlyList<int>? _retained;

    public CreditConsumptionTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetLiveAsync(TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);
        _subscriptions
            .Setup(repository => repository.TryAppendEventAsync(
                TenantId, SubscriptionId, It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _closures
            .Setup(repository => repository.TryAcquireClaimAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UsageClaimOutcome.Acquired);

        _usage
            .Setup(repository => repository.TryAppendRecordAsync(It.IsAny<SubscriptionUsageRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // One counter, kept like the real one: opened with the seed's snapshot, then moved.
        _usage
            .Setup(repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageCounter, decimal, CancellationToken>((seed, delta, _) =>
            {
                _counter ??= seed;
                _counter.Balance += delta;
                return Task.FromResult(_counter);
            });
        _usage
            .Setup(repository => repository.AddPurchasedConsumedAsync(
                TenantId, It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, decimal, CancellationToken>((_, _, delta, _) =>
            {
                _counter!.PurchasedConsumed += delta;
                return Task.FromResult<SubscriptionUsageCounter?>(_counter);
            });
        _usage
            .Setup(repository => repository.GetCounterAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _counter);
        _usage
            .Setup(repository => repository.TryRetainThresholdsAsync(
                TenantId, It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<int>, CancellationToken>((_, _, retained, _) => _retained = retained)
            .ReturnsAsync(true);

        _projection
            .Setup(publisher => publisher.PublishAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<PlanMeter>(), It.IsAny<BillingPeriod>(),
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<SubscriptionUsageCurrentSubLimit>?>()))
            .ReturnsAsync(UsageProjectionOutcome.Published);

        _events
            .Setup(factory => factory.CreateCreditEvent(
                It.IsAny<SubscriptionDetail>(), It.IsAny<string>(), It.IsAny<SubscriptionCreditGrant>(),
                It.IsAny<string>(), It.IsAny<int?>()))
            .Returns((SubscriptionDetail _, string type, SubscriptionCreditGrant _, string _, int? _) =>
            {
                _published.Add(type);
                return new SubscriptionOutboxEvent { EventType = type };
            });
    }

    [Fact]
    public async Task The_plans_allowance_is_spent_before_bought_credits()
    {
        Opened(balance: 95);
        _grants.All.Add(Grant("g1", remaining: 50));

        var result = await Service().RecordAsync(Record(10), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Used.Should().Be(105);
        result.Value.PurchasedConsumed.Should().Be(5,
            "only the five units past the plan's allowance are paid for with credits (AC-10)");
        result.Value.PurchasedRemaining.Should().Be(45);
        result.Value.Overage.Should().Be(0, "usage paid for with credits is not overage");
        _grants.All.Single().Remaining.Should().Be(45);
    }

    [Fact]
    public async Task A_recording_inside_the_allowance_never_reads_a_grant_to_decide()
    {
        Opened(balance: 10);
        _grants.All.Add(Grant("g1", remaining: 50));

        await Service().RecordAsync(Record(10), "corr-1", CancellationToken.None);

        _grants.All.Single().Remaining.Should().Be(50, "the plan's allowance is lost at window end and must go first");
        _counter!.PurchasedConsumed.Should().Be(0);
    }

    [Fact]
    public async Task A_recording_credits_cannot_fully_cover_is_refused_and_changes_nothing()
    {
        _subscription.Plan.Meters[0].OverageAllowed = false;
        Opened(balance: 100);
        _grants.All.Add(Grant("g1", remaining: 3));

        var result = await Service().RecordAsync(Record(5), "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeFalse("partly paid for is still past what the subscriber may use (AC-11)");
        _grants.All.Single().Remaining.Should().Be(3, "a refused recording must not keep the credits it drew");
        _grants.All.Single().Status.Should().Be(CreditGrantStatus.Active);
        _counter!.PurchasedConsumed.Should().Be(0);
        _counter.Balance.Should().Be(100);
    }

    [Fact]
    public async Task A_concurrent_recording_at_the_boundary_does_not_refuse_one_its_own_credits_paid_for()
    {
        _subscription.Plan.Meters[0].OverageAllowed = false;
        Opened(balance: 99);
        _grants.All.Add(Grant("g1", remaining: 10));

        // Every read-back after the draw already holds another recording's +2, but not the credits
        // that recording is about to draw for itself, which is what a real concurrent read sees.
        _usage
            .Setup(repository => repository.AddPurchasedConsumedAsync(
                TenantId, It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, decimal, CancellationToken>((_, _, delta, _) =>
            {
                _counter!.PurchasedConsumed += delta;
                return Task.FromResult<SubscriptionUsageCounter?>(new SubscriptionUsageCounter
                {
                    ItemId = _counter.ItemId,
                    TenantId = TenantId,
                    SubscriptionId = SubscriptionId,
                    MeterKey = _counter.MeterKey,
                    LimitSnapshot = _counter.LimitSnapshot,
                    PurchasedConsumed = _counter.PurchasedConsumed,
                    Balance = _counter.Balance + 2
                });
            });

        var result = await Service().RecordAsync(Record(2), "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeTrue(
            "101 against 100 plus the 1 credit this recording drew is covered; another recording's increment is not its to answer for");
        _grants.All.Single().Remaining.Should().Be(9, "the credit it paid with stays spent");
    }

    [Fact]
    public async Task A_caller_that_cancels_mid_draw_never_leaves_credits_spent_that_the_counter_does_not_show()
    {
        Opened(balance: 100);
        _grants.All.Add(Grant("g1", remaining: 10));
        using var cancellation = new CancellationTokenSource();

        // The caller goes away the moment the counter has moved, and every write afterwards that
        // still listens to it fails the way a real driver call would.
        _usage
            .Setup(repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageCounter, decimal, CancellationToken>((seed, delta, _) =>
            {
                _counter ??= seed;
                _counter.Balance += delta;
                cancellation.Cancel();
                return Task.FromResult(_counter);
            });
        _usage
            .Setup(repository => repository.AddPurchasedConsumedAsync(
                TenantId, It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, decimal, CancellationToken>((_, _, delta, token) =>
            {
                token.ThrowIfCancellationRequested();
                _counter!.PurchasedConsumed += delta;
                return Task.FromResult<SubscriptionUsageCounter?>(_counter);
            });

        try
        {
            await Service().RecordAsync(Record(5), "corr-1", cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Whatever the call itself answers, the ledger and the counter must still agree.
        }

        var spent = 10 - _grants.All.Single().Remaining;
        _counter!.PurchasedConsumed.Should().BeGreaterThanOrEqualTo(spent,
            "credits spent without the counter knowing are billed again as overage at period close");
    }

    [Fact]
    public async Task Credits_may_take_usage_past_a_meter_that_allows_no_overage()
    {
        _subscription.Plan.Meters[0].OverageAllowed = false;
        Opened(balance: 100);
        _grants.All.Add(Grant("g1", remaining: 10));

        var result = await Service().RecordAsync(Record(5), "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeTrue("bought credits are allowance, not overage");
        result.Value.Used.Should().Be(105);
    }

    [Fact]
    public async Task What_credits_cannot_cover_becomes_overage_where_overage_is_allowed()
    {
        Opened(balance: 100);
        _grants.All.Add(Grant("g1", remaining: 3));

        var result = await Service().RecordAsync(Record(5), "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeTrue();
        result.Value.PurchasedConsumed.Should().Be(3);
        result.Value.Overage.Should().Be(2, "AC-12");
        _grants.All.Single().Status.Should().Be(CreditGrantStatus.Exhausted);
        _published.Should().Equal(SubscriptionConstants.SubscriptionCreditsExhausted);
    }

    [Fact]
    public async Task Credits_that_expire_tonight_are_spent_before_credits_that_never_expire()
    {
        Opened(balance: 100);
        _grants.All.Add(Grant("never", remaining: 10, policy: MeterResetPolicy.Never));
        _grants.All.Add(Grant("tonight", remaining: 10, expiresAt: Now.UtcDateTime.AddHours(6)));

        await Service().RecordAsync(Record(4), "corr-1", CancellationToken.None);

        _grants.All.Single(grant => grant.ItemId == "tonight").Remaining.Should().Be(6, "AC-13");
        _grants.All.Single(grant => grant.ItemId == "never").Remaining.Should().Be(10);
    }

    [Fact]
    public async Task A_seat_spends_its_own_credits_before_the_pools()
    {
        _grants.All.Add(Grant("seat", remaining: 5, seat: 2));
        _grants.All.Add(Grant("pool", remaining: 10));

        var draw = await Ledger().DrawAsync(_subscription, "ai-credit", 2, 8, Now.UtcDateTime, CancellationToken.None);

        draw.Drawn.Should().Be(8);
        _grants.All.Single(grant => grant.ItemId == "seat").Remaining.Should().Be(0);
        _grants.All.Single(grant => grant.ItemId == "pool").Remaining.Should().Be(7,
            "the pool is a top-up any seat may spend once its own credits are gone");
    }

    [Fact]
    public async Task One_seats_credits_are_never_spent_by_another_seat()
    {
        _grants.All.Add(Grant("seat-1", remaining: 5, seat: 1));

        var draw = await Ledger().DrawAsync(_subscription, "ai-credit", 2, 3, Now.UtcDateTime, CancellationToken.None);

        draw.Drawn.Should().Be(0, "AC-19");
    }

    [Fact]
    public void Period_close_does_not_bill_credit_paid_usage_as_overage()
    {
        var counter = new SubscriptionUsageCounter { LimitSnapshot = 100, PurchasedConsumed = 20, Balance = 130 };

        var allowance = MeterAllowance.Effective(counter, 0);

        (counter.Balance - allowance).Should().Be(10,
            "rating measures overage against this figure, and the 20 units credits paid for were already paid");
    }

    [Fact]
    public async Task The_allowance_resolver_counts_credits_spent_in_the_window()
    {
        var counter = new SubscriptionUsageCounter { LimitSnapshot = 100, PurchasedConsumed = 20 };

        var allowance = await new MeterAllowanceResolver(_usage.Object).EffectiveAsync(
            _subscription, _subscription.Plan.Meters[0], default, counter, CancellationToken.None);

        allowance.Should().Be(120, "rating and the overage preview both read the allowance from here");
    }

    [Fact]
    public async Task Buying_credits_re_arms_a_threshold_they_moved_out_of_reach()
    {
        Opened(balance: 85);
        _counter!.NotifiedThresholds = [80];
        _grants.All.Add(Grant("g1", remaining: 100));

        await Ledger().RearmThresholdsAsync(
            _subscription, _subscription.Plan.Meters[0], null, Now.UtcDateTime, CancellationToken.None);

        _retained.Should().BeEmpty("85 of 200 is under 80%, so the alert must be able to fire again at 160 (AC-24)");
    }

    [Fact]
    public async Task Thresholds_are_measured_against_the_plan_and_credits_together()
    {
        var usage = new Mock<ISubscriptionUsageRepository>();
        usage
            .Setup(repository => repository.TryMarkThresholdNotifiedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var evaluator = new UsageThresholdEvaluator(
            usage.Object, _subscriptions.Object, new SubscriptionOutboxEventFactory(),
            NullLogger<UsageThresholdEvaluator>.Instance);
        var counter = new SubscriptionUsageCounter
        {
            TenantId = TenantId, MeterKey = "ai-credit", LimitSnapshot = 100, PurchasedConsumed = 60, Balance = 160
        };

        var reported = await evaluator.EvaluateAsync(_subscription, counter, "corr-1", CancellationToken.None, liveCredits: 40);

        reported.Should().Be(1, "160 of 100 + 60 + 40 is exactly 80%, and only 80 has been reached");
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
        new RecordUsageRequestValidator(new SubscriptionOptionsMonitorStub(new SubscriptionOptions())),
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<UsageRecordingService>.Instance,
        new ControlledTimeProvider(Now),
        credits: Ledger());

    private CreditLedger Ledger() =>
        new(_grants, _usage.Object, _subscriptions.Object, _events.Object, _scheduler.Object);

    /// <summary>A window already open at <paramref name="balance"/> against a plan allowance of 100.</summary>
    private void Opened(decimal balance) =>
        _counter = new SubscriptionUsageCounter
        {
            ItemId = "counter-1",
            TenantId = TenantId,
            SubscriptionId = SubscriptionId,
            MeterKey = "ai-credit",
            LimitSnapshot = 100,
            Balance = balance
        };

    private static RecordUsageRequest Record(decimal quantity) => new()
    {
        MeterKey = "ai-credit",
        Quantity = quantity,
        IdempotencyKey = $"use-{Guid.NewGuid():N}",
        Enforce = true
    };

    private static SubscriptionCreditGrant Grant(
        string id,
        decimal remaining,
        int? seat = null,
        MeterResetPolicy policy = MeterResetPolicy.CarryForward,
        DateTime? expiresAt = null) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        SubscriptionId = SubscriptionId,
        MeterKey = "ai-credit",
        SeatNumber = seat,
        Quantity = remaining,
        Remaining = remaining,
        ExpiryPolicy = expiresAt is null ? policy : MeterResetPolicy.Periodic,
        ExpiresAtUtc = expiresAt,
        GrantedAtUtc = Now.UtcDateTime.AddDays(-1)
    };

    private static SubscriptionDetail Subscription() => new()
    {
        ItemId = SubscriptionId,
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "USD",
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        },
        Plan = new PlanSnapshot
        {
            Code = "pro",
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "ai-credit",
                    UnitLabel = "credit",
                    IncludedQuantity = 100,
                    OverageAllowed = true,
                    ThresholdPercents = [80, 100],
                    TopUp = new MeterTopUpTerms { Enabled = true, MaxQuantity = 1_000 }
                }
            ]
        }
    };
}
