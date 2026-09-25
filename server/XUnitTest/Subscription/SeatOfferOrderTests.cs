using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Which free seat a newcomer is given.
/// </summary>
/// <remarks>
/// Guards handing somebody a seat that is already spent. A seat keeps whatever the last person left
/// of its allowance and that balance rides to the period boundary, so free seats are not
/// interchangeable: a newcomer put into a spent seat while an untouched one sits empty beside it is
/// short for the rest of the period, with nothing in the product to tell them why.
/// <para>
/// The ordering is also what must not change for an organization-wise plan or a subscription nobody
/// has used, where every free seat is equal and the lowest number is what an administrator can
/// predict.
/// </para>
/// </remarks>
public sealed class SeatOfferOrderTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";
    private const string MeterKey = "ai_tokens";
    private const string PeriodKey = "M20260901T000000Z";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionAssignmentRepository> _assignments = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));

    private readonly Dictionary<string, SubscriptionUsageCounter> _counters =
        new(StringComparer.Ordinal);

    private readonly List<SubscriptionAssignment> _written = [];

    public SeatOfferOrderTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "admin")));

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Metered);

        _assignments
            .Setup(repository => repository.ListActiveAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .Callback((SubscriptionAssignment assignment, CancellationToken _) =>
                _written.Add(assignment))
            .ReturnsAsync(MemberAssignmentOutcome.Assigned);

        _usage
            .Setup(repository => repository.GetCountersAsync(
                TenantId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyCollection<string>, CancellationToken>((_, ids, _) =>
                Task.FromResult<IReadOnlyDictionary<string, SubscriptionUsageCounter>>(
                    ids.Where(_counters.ContainsKey)
                        .ToDictionary(id => id, id => _counters[id], StringComparer.Ordinal)));
    }

    [Fact]
    public async Task A_newcomer_is_given_the_seat_with_the_most_left()
    {
        GivenSpent(seat: 1, 900);
        GivenSpent(seat: 2, 10);
        GivenSpent(seat: 3, 400);

        await Assign("newcomer");

        _written.Should().ContainSingle().Which.SeatNumber.Should().Be(2,
            because: "seat one is nearly spent and its balance rides to the period boundary, so " +
                     "whoever took it would be short for the rest of the period");
    }

    [Fact]
    public async Task An_untouched_seat_beats_one_somebody_has_used()
    {
        GivenSpent(seat: 1, 5);

        await Assign("newcomer");

        _written.Should().ContainSingle().Which.SeatNumber.Should().Be(2,
            because: "a seat nobody has drawn on has the whole allowance left, and passing it " +
                     "over to reuse a spent one is the case this rule exists for");
    }

    /// <summary>
    /// Every subscription that has just been bought, and every organization-wise one.
    /// </summary>
    [Fact]
    public async Task Seats_nobody_has_used_are_given_out_lowest_first()
    {
        await Assign("first", "second");

        _written.Select(assignment => assignment.SeatNumber)
            .Should().BeEquivalentTo([1, 2], options => options.WithStrictOrdering(),
                because: "equal seats have to be handed out in the order an administrator can " +
                         "predict, and this is what the endpoint did before seats were ranked");
    }

    [Fact]
    public async Task A_batch_gives_each_person_a_seat_of_their_own_best_first()
    {
        GivenSpent(seat: 1, 900);
        GivenSpent(seat: 2, 10);
        GivenSpent(seat: 3, 400);

        await Assign("first", "second");

        _written.Select(assignment => assignment.SeatNumber)
            .Should().BeEquivalentTo([2, 3], options => options.WithStrictOrdering(),
                because: "the order is settled once for the batch, and a seat handed to two " +
                         "people would be refused by the index with nothing to say why");
    }

    /// <summary>
    /// A seat that saved last window opens this one with more than the plan includes.
    /// </summary>
    /// <remarks>
    /// Ranking on what has been spent rather than on what is left would rank a carried-forward
    /// seat by its balance alone and hand out the poorer of the two.
    /// </remarks>
    [Fact]
    public async Task A_seat_carrying_leftovers_forward_outranks_one_that_merely_looks_cheaper()
    {
        // Seat 1 spent 100 but opened with 1,100 carried forward, leaving 1,000.
        GivenSpent(seat: 1, 100, opened: 1_100);

        // Seat 2 spent 50 of the plan's own 1,000, leaving 950 — less, despite the smaller spend.
        GivenSpent(seat: 2, 50);

        await Assign("newcomer");

        _written.Should().ContainSingle().Which.SeatNumber.Should().Be(1,
            because: "what a newcomer can actually spend is what is left, not what the last " +
                     "person happened to use");
    }

    private void GivenSpent(int seat, decimal balance, decimal? opened = null)
    {
        var id = SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, PeriodKey, seat);

        _counters[id] = new SubscriptionUsageCounter
        {
            ItemId = id,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            SubscriptionId = SubscriptionId,
            MeterKey = MeterKey,
            SeatNumber = seat,
            Balance = balance,
            LimitSnapshot = opened,
            AppliedRecordCount = 1,
            ExpiresAtUtc = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    private async Task Assign(params string[] userIds) =>
        await Service().AssignAsync(
            SubscriptionId,
            new AssignMemberRequest { UserIds = [.. userIds] },
            "corr-1",
            CancellationToken.None);

    private SubscriptionMemberService Service() => new(
        _subscriptions.Object,
        _assignments.Object,
        _contextResolver.Object,
        new EntitlementSnapshotCache(new OptionsStub(), _time),
        _time,
        _usage.Object,
        new MeterAllowanceResolver(_usage.Object));

    private static SubscriptionDetail Metered => new()
    {
        ItemId = SubscriptionId,
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
        QuantityItems =
        [
            new SubscriptionQuantityItem
            {
                ItemKey = "seat",
                UnitLabel = "seat",
                Quantity = 3,
                CountsMembers = true
            }
        ],
        Price = new PriceSnapshot { QuantityItemKey = "seat" },
        Plan = new PlanSnapshot
        {
            Code = "starter",
            SubscriberScope = SubscriberScope.User,
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = MeterKey,
                    UnitLabel = "token",
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
