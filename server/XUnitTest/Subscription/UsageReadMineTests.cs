using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// What a caller is shown of their own allowance.
/// </summary>
/// <remarks>
/// Guards showing somebody a balance they are not the one spending. A seat carries its own
/// allowance, so a member reading the organization's shared pool sees a number that neither tells
/// them how much they have left nor moves when they spend it — and one shown twice, once per plan,
/// cannot tell which of the two the next call will draw down.
/// <para>
/// The organization's own read is covered elsewhere and must stay exactly as it was: every
/// subscriber in production reads it, and none of them holds a seat.
/// </para>
/// </remarks>
public sealed class UsageReadMineTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string UserId = "user-a";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<IUsagePeriodClosureRepository> _closures = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IUsageThresholdEvaluator> _thresholds = new();
    private readonly Mock<IUsageProjectionPublisher> _projection = new();
    private readonly Mock<ISubscriptionUsageCurrentRepository> _current = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly Mock<ISubscriberSubscriptionResolver> _resolver = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));

    private readonly Dictionary<string, SubscriptionUsageCounter> _counters =
        new(StringComparer.Ordinal);

    public UsageReadMineTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", UserId)));

        _resolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<SubscriptionContext>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _usage
            .Setup(repository => repository.GetCountersAsync(
                TenantId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyCollection<string>, CancellationToken>((_, ids, _) =>
                Task.FromResult<IReadOnlyDictionary<string, SubscriptionUsageCounter>>(
                    ids.Where(_counters.ContainsKey)
                        .ToDictionary(id => id, id => _counters[id], StringComparer.Ordinal)));
    }

    [Fact]
    public async Task A_member_is_shown_their_own_seats_balance_and_not_the_organizations()
    {
        GivenResolved(
            new ResolvedSubscription(Metered("sub-seats", "ai_tokens"), SeatNumber: 2));

        GivenBalance("sub-seats", "ai_tokens", seat: 2, balance: 30);
        GivenBalance("sub-seats", "ai_tokens", seat: null, balance: 900);

        var items = await Read();

        items.Should().ContainSingle().Which.Used.Should().Be(30,
            because: "the subscription's own pool is not what this person spends, and showing it " +
                     "would tell them they had nearly nothing left of an allowance they had " +
                     "barely touched");
    }

    [Fact]
    public async Task Two_members_of_one_subscription_are_shown_different_balances()
    {
        GivenBalance("sub-seats", "ai_tokens", seat: 1, balance: 10);
        GivenBalance("sub-seats", "ai_tokens", seat: 2, balance: 70);

        GivenResolved(new ResolvedSubscription(Metered("sub-seats", "ai_tokens"), SeatNumber: 1));
        var first = await Read();

        GivenResolved(new ResolvedSubscription(Metered("sub-seats", "ai_tokens"), SeatNumber: 2));
        var second = await Read();

        first[0].Used.Should().Be(10);
        second[0].Used.Should().Be(70,
            because: "five seats on a plan including ten million tokens is ten million each, and " +
                     "one balance for both would be the pooled read under another name");
    }

    [Fact]
    public async Task A_meter_only_the_organization_sells_is_still_reported()
    {
        GivenResolved(
            new ResolvedSubscription(Metered("sub-seats", "ai_tokens"), SeatNumber: 2),
            new ResolvedSubscription(Metered("sub-org", "widgets"), SeatNumber: null));

        GivenBalance("sub-org", "widgets", seat: null, balance: 5);

        var items = await Read();

        items.Select(item => item.MeterKey).Should().BeEquivalentTo(["ai_tokens", "widgets"],
            because: "being given an allowance of one's own does not stop somebody using what " +
                     "the organization is still paying for");
    }

    [Fact]
    public async Task A_meter_both_plans_sell_is_reported_once_from_the_seat()
    {
        GivenResolved(
            new ResolvedSubscription(Metered("sub-seats", "ai_tokens"), SeatNumber: 2),
            new ResolvedSubscription(Metered("sub-org", "ai_tokens"), SeatNumber: null));

        GivenBalance("sub-seats", "ai_tokens", seat: 2, balance: 30);
        GivenBalance("sub-org", "ai_tokens", seat: null, balance: 900);

        var items = await Read();

        items.Should().ContainSingle(because:
                "two rows for one meter leave a reader no way to tell which of them the next call " +
                "draws down")
            .Which.Used.Should().Be(30,
                because: "a recording would spend the seat, so the seat is the balance to show");
    }

    [Fact]
    public async Task A_caller_holding_nothing_is_told_so_rather_than_shown_an_empty_allowance()
    {
        var result = await Service().ReadMineAsync(null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_not_found",
            because: "an empty list reads as 'you have spent nothing', which is the opposite of " +
                     "'you have nothing to spend'");
    }

    private async Task<IReadOnlyList<UsageResponse>> Read()
    {
        var result = await Service().ReadMineAsync(null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        return result.Value!;
    }

    private void GivenResolved(params ResolvedSubscription[] resolved) =>
        _resolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<SubscriptionContext>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);

    private void GivenBalance(
        string subscriptionId,
        string meterKey,
        int? seat,
        decimal balance)
    {
        var id = SubscriptionUsageCounter.CreateId(
            subscriptionId, meterKey, "M20260901T000000Z", seat);

        _counters[id] = new SubscriptionUsageCounter
        {
            ItemId = id,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            SubscriptionId = subscriptionId,
            MeterKey = meterKey,
            SeatNumber = seat,
            Balance = balance,
            AppliedRecordCount = 1,
            ExpiresAtUtc = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };
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
        _time,
        metrics: null,
        resolver: _resolver.Object);

    private static SubscriptionDetail Metered(string subscriptionId, string meterKey) => new()
    {
        ItemId = subscriptionId,
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        CurrentPeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            AnchorDayOfMonth = 1
        },
        Plan = new PlanSnapshot
        {
            Code = "plan",
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = meterKey,
                    UnitLabel = "unit",
                    IncludedQuantity = 1_000,
                    OverageAllowed = false
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
