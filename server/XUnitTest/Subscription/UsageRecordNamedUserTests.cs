using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// A client-credentials caller recording usage on a named person's behalf.
/// </summary>
/// <remarks>
/// Guards three ways of billing the wrong allowance. A client that names somebody holding a place
/// must spend that place and file the entry as theirs, or the per-person row the projection rebuilds
/// from the ledger drops it. A client naming somebody who holds no place on a plan sold per person
/// must be refused, not quietly charged to the organization's shared pool. And a signed-in caller
/// must never be able to spend a colleague's place by naming them.
/// </remarks>
public sealed class UsageRecordNamedUserTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string MeterKey = "ai_tokens";
    private const string SeatedUser = "user-a";

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

    private readonly List<SubscriptionUsageRecord> _ledger = [];
    private readonly List<string?> _resolvedFor = [];

    private readonly SubscriptionDetail _memberPlan = Metered("sub-members", SubscriberScope.User);
    private SubscriptionDetail? _organizationPlan = Metered("sub-org", SubscriberScope.Organization);
    private IReadOnlyList<SubscriptionDetail> _memberBased = [];

    public UsageRecordNamedUserTests()
    {
        GivenCaller(userId: null);

        _memberBased = [_memberPlan];

        _subscriptions
            .Setup(repository => repository.ListLiveMemberBasedAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _memberBased);

        // Only user-a holds a place, and only on the member plan; everybody draws on the
        // organization's own plan after it, in the order the real resolver answers.
        _resolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<SubscriptionContext>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionContext context, DateTime _, CancellationToken _) =>
            {
                _resolvedFor.Add(context.UserId);

                var resolved = new List<ResolvedSubscription>();

                if (context.UserId == SeatedUser)
                {
                    resolved.Add(new ResolvedSubscription(_memberPlan, SeatNumber: 2));
                }

                if (_organizationPlan is not null)
                {
                    resolved.Add(new ResolvedSubscription(_organizationPlan, SeatNumber: null));
                }

                return resolved;
            });

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
                _ledger.Add(record);

                return Task.FromResult(true);
            });

        _usage
            .Setup(repository => repository.ApplyDeltaAsync(
                It.IsAny<SubscriptionUsageCounter>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .Returns<SubscriptionUsageCounter, decimal, CancellationToken>((seed, delta, _) =>
            {
                seed.Balance += delta;

                return Task.FromResult(seed);
            });
    }

    [Fact]
    public async Task A_client_naming_a_place_holder_spends_their_place_and_files_it_as_theirs()
    {
        var result = await Record(userId: SeatedUser);

        result.IsSuccess.Should().BeTrue();

        var entry = _ledger.Should().ContainSingle().Subject;

        entry.SubscriptionId.Should().Be("sub-members",
            because: "the person was given an allowance of their own, and spending the " +
                     "organization's instead leaves theirs untouched while the shared pool runs out");
        entry.SeatNumber.Should().Be(2);
        entry.RecordedByUserId.Should().Be(SeatedUser,
            because: "the per-person row is rebuilt from this field, and an entry filed under " +
                     "nobody vanishes from that person's usage at the next repair");
    }

    [Fact]
    public async Task A_client_naming_somebody_without_a_place_on_a_plan_sold_per_person_is_refused()
    {
        var result = await Record(userId: "user-without-a-place");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_member_seat_not_found",
            because: "the caller asked for this person, and charging the organization instead " +
                     "would pass a mistyped id off as a successful call");
        _ledger.Should().BeEmpty(because: "a refused call must not have spent anything");
    }

    [Fact]
    public async Task A_refusal_names_the_missing_place_even_when_the_organization_has_no_plan_of_its_own()
    {
        _organizationPlan = null;

        var result = await Record(userId: "user-without-a-place");

        result.ErrorCode.Should().Be("subscription_member_seat_not_found",
            because: "'you have no subscription' would send support looking for a missing " +
                     "purchase when the problem is a missing assignment");
    }

    [Fact]
    public async Task A_client_naming_somebody_where_only_the_organization_meters_the_key_charges_the_organization()
    {
        _memberBased = [];

        var result = await Record(userId: "user-without-a-place");

        result.IsSuccess.Should().BeTrue(
            because: "nothing is sold per person for this meter, so the usage is the " +
                     "organization's exactly as it would be with nobody named");

        var entry = _ledger.Should().ContainSingle().Subject;

        entry.SubscriptionId.Should().Be("sub-org");
        entry.SeatNumber.Should().BeNull();
        entry.RecordedByUserId.Should().BeNull(
            because: "the entry is the organization's, and filing it under a person would give " +
                     "them a usage row for an allowance they do not hold");
    }

    [Fact]
    public async Task A_signed_in_caller_cannot_spend_a_colleagues_place_by_naming_them()
    {
        GivenCaller(userId: "user-b");

        var result = await Record(userId: SeatedUser);

        result.IsSuccess.Should().BeTrue();
        _resolvedFor.Should().Equal(["user-b"],
            because: "the token is the stronger claim, and trusting the body would let anybody " +
                     "in the organization spend anybody else's allowance");
        _ledger.Should().ContainSingle().Which.SubscriptionId.Should().Be("sub-org");
    }

    [Fact]
    public async Task A_client_naming_nobody_records_exactly_as_it_always_has()
    {
        var result = await Record(userId: null);

        result.IsSuccess.Should().BeTrue();
        _resolvedFor.Should().Equal([(string?)null]);

        var entry = _ledger.Should().ContainSingle().Subject;

        entry.SubscriptionId.Should().Be("sub-org",
            because: "every existing client sends no user, and its usage must keep landing on " +
                     "the organization's plan");
        entry.RecordedByUserId.Should().BeNull();
        _subscriptions.Verify(
            repository => repository.ListLiveMemberBasedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never(),
            "a caller naming nobody should not pay for a query it cannot be refused by");
    }

    private void GivenCaller(string? userId) =>
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(
                    TenantId, OrganizationId, userId ?? "client:app-1", userId)));

    private async Task<SubscriptionOperationResult<UsageResponse>> Record(string? userId) =>
        await Service().RecordAsync(
            new RecordUsageRequest
            {
                MeterKey = MeterKey,
                Quantity = 1,
                IdempotencyKey = "idem-1",
                UserId = userId
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
        _time,
        metrics: null,
        resolver: _resolver.Object);

    private static SubscriptionDetail Metered(string subscriptionId, SubscriberScope scope) => new()
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
            Code = subscriptionId,
            SubscriberScope = scope,
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = MeterKey,
                    UnitLabel = "token",
                    IncludedQuantity = 1_000,
                    OverageAllowed = true
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
