using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// The question asked on every gated action.
/// </summary>
public sealed class EntitlementServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail? _subscription = NewSubscription();
    private long _balance;
    private int _reads;

    public EntitlementServiceTests()
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
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                _reads++;

                return _subscription;
            });

        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SubscriptionUsageCounter { Balance = _balance });
    }

    /// <summary>
    /// The guarantee this module exists to make, checked where it cannot be argued with.
    /// </summary>
    /// <remarks>
    /// If the provider is unreachable, every existing customer must keep working. The way that
    /// is enforced is that entitlement has no way to call one — so this asserts the dependency
    /// list rather than any behaviour, because behaviour can be added back by accident.
    /// </remarks>
    [Fact]
    public void Entitlement_cannot_call_a_payment_provider()
    {
        var dependencies = typeof(EntitlementService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        dependencies.Should().NotContain(name =>
            name.Contains("Gateway", StringComparison.Ordinal) ||
            name.Contains("HttpClient", StringComparison.Ordinal) ||
            name.Contains("PaymentService", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_organization_with_no_subscription_is_granted_nothing()
    {
        _subscription = null;

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.HasSubscription.Should().BeFalse();
        result.Value.Entitlements.Should().BeEmpty();
    }

    [Fact]
    public async Task A_canceled_subscription_grants_nothing()
    {
        _subscription!.Status = SubscriptionStatus.Canceled;

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements.Should().BeEmpty();
        result.Value.Status.Should().Be(nameof(SubscriptionStatus.Canceled));
    }

    [Fact]
    public async Task A_scheduled_cancellation_stops_granting_at_its_promised_boundary_even_while_status_is_still_active()
    {
        _subscription!.CancelAtPeriodEnd = true;
        _subscription.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        // The clock advances to September 4 — three days past the boundary — while the
        // finalizing worker has not run yet, so Status is still Active.
        _time.Advance(new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc) - _time.GetUtcNow().UtcDateTime);

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements.Should().BeEmpty(
            "access stops at the promised boundary regardless of whether the worker has caught " +
            "up to it yet");
    }

    [Fact]
    public async Task A_scheduled_cancellation_still_grants_before_its_promised_boundary()
    {
        _subscription!.CancelAtPeriodEnd = true;
        _subscription.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements.Should().NotBeEmpty(
            "a scheduled cancellation keeps granting through what was paid for");
    }

    [Fact]
    public async Task A_cached_entitlement_stops_granting_the_instant_the_boundary_passes_even_before_the_cache_expires()
    {
        _subscription!.CancelAtPeriodEnd = true;
        _subscription.CurrentPeriodEndUtc = _time.GetUtcNow().UtcDateTime.AddMilliseconds(500);
        var service = Service();

        var before = await service.GetAsync(false, null, "corr-1", CancellationToken.None);
        before.Value!.Entitlements.Should().NotBeEmpty("still inside the paid period");

        // Well inside the cache's own TTL (EntitlementCacheSeconds defaults to 10), so this read
        // is served from cache — the same in-memory SubscriptionDetail as the read above.
        _time.Advance(TimeSpan.FromSeconds(1));

        var after = await service.GetAsync(false, null, "corr-2", CancellationToken.None);

        after.Value!.Entitlements.Should().BeEmpty(
            "the cached snapshot is re-evaluated against the current instant on every read, not " +
            "only re-fetched when the cache entry itself expires");
        _reads.Should().Be(1, "this was genuinely served from cache, not a fresh read that " +
                              "happened to agree");
    }

    [Fact]
    public async Task A_past_due_subscription_still_grants_during_the_grace_period()
    {
        _subscription!.Status = SubscriptionStatus.PastDue;

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements.Should().NotBeEmpty(
            "cutting a customer off the moment a renewal fails punishes an expired card");
    }

    [Fact]
    public async Task A_counted_entitlement_reports_what_is_left()
    {
        _balance = 487;

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        var entitlement = result.Value!.Entitlements.Single();
        entitlement.Allowed.Should().BeTrue();
        entitlement.Used.Should().Be(487);
        entitlement.Remaining.Should().Be(13);
    }

    [Fact]
    public async Task A_lifetime_entitlement_reads_the_counter_that_survives_renewal()
    {
        _subscription!.Plan.Meters[0].ResetPolicy = MeterResetPolicy.Never;
        _balance = 300;

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements.Single().Remaining.Should().Be(200);
        _usage.Verify(repository => repository.GetCounterAsync(
            TenantId,
            SubscriptionUsageCounter.CreateId(
                _subscription.ItemId,
                "screening",
                MeterPeriodResolver.LifetimePeriodKey),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_counted_entitlement_at_its_limit_reports_the_reason()
    {
        _balance = 500;

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        var entitlement = result.Value!.Entitlements.Single();
        entitlement.Allowed.Should().BeFalse();
        entitlement.Reason.Should().Be(nameof(EntitlementReason.LimitReached));
        entitlement.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task An_unlimited_entitlement_never_reports_a_limit_reached()
    {
        _subscription!.Plan.Entitlements =
        [
            new PlanEntitlement
            {
                Key = "api_access",
                LimitKind = EntitlementLimitKind.Unlimited
            }
        ];
        _balance = long.MaxValue / 2;

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements.Single().Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task The_decision_carries_the_products_own_unit_label()
    {
        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements.Single().UnitLabel.Should().Be("screening");
        result.Value.Quantities.Single().UnitLabel.Should().Be("seat");
    }

    [Fact]
    public async Task Plan_features_pass_through_untouched()
    {
        _subscription!.Plan.FeaturesJson = """{"qualified_signature":true}""";

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.FeaturesJson.Should().Be("""{"qualified_signature":true}""");
    }

    [Fact]
    public async Task A_trials_grant_replaces_the_plans_limit()
    {
        _subscription!.Status = SubscriptionStatus.Trialing;
        _subscription.Trial = new TrialTerms
        {
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddDays(14),
            Grants = [new TrialMeterGrant { MeterKey = "screening", IncludedQuantity = 25 }]
        };

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements.Single().Limit.Should().Be(25,
            "entitlement and usage recording must measure the same allowance, or a caller is " +
            "told it may act and then refused");
    }

    [Fact]
    public async Task An_unknown_key_says_it_is_not_on_the_plan()
    {
        var result = await Service().GetAsync(
            "not_a_feature", false, null, "corr-1", CancellationToken.None);

        result.Value!.Allowed.Should().BeFalse();
        result.Value.Reason.Should().Be(nameof(EntitlementReason.NotInPlan));
    }

    [Fact]
    public async Task An_unknown_key_without_a_subscription_says_so_instead()
    {
        _subscription = null;

        var result = await Service().GetAsync(
            "pep_screening", false, null, "corr-1", CancellationToken.None);

        result.Value!.Reason.Should().Be(nameof(EntitlementReason.NoSubscription),
            "the reason sends a support engineer to a different place than 'not on this plan'");
    }

    [Fact]
    public async Task Repeated_reads_are_served_from_the_cache()
    {
        var service = Service();

        await service.GetAsync(false, null, "corr-1", CancellationToken.None);
        await service.GetAsync(false, null, "corr-2", CancellationToken.None);

        _reads.Should().Be(1);
    }

    [Fact]
    public async Task A_fresh_read_bypasses_the_cache()
    {
        var service = Service();

        await service.GetAsync(false, null, "corr-1", CancellationToken.None);
        await service.GetAsync(true, null, "corr-2", CancellationToken.None);

        _reads.Should().Be(2, "a caller about to do something irreversible can insist");
    }

    [Fact]
    public async Task A_caller_without_an_organization_is_refused()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Unavailable,
                "subscription_organization_missing",
                "An organization is required."));

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
    }

    [Fact]
    public async Task A_requested_organization_is_forwarded_to_context_resolution()
    {
        await Service().GetAsync(false, "org-9", "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    [Fact]
    public async Task A_carried_allowance_is_advertised_before_any_usage_opens_the_window()
    {
        // The window the clock sits in has recorded nothing, so it has no counter and no frozen
        // snapshot. Answered from the plan alone, this advertised 500 while the usage gate would
        // have allowed 900 — and the advertised figure then jumped the moment somebody recorded
        // their first screening. An entitlement that moves because the product was used is not an
        // entitlement.
        GivenCarryForward(cap: null);
        GivenPreviousWindow(balance: 100, limitSnapshot: 500);

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements
            .Single(entitlement => entitlement.Key == "pep_screening")
            .Limit
            .Should()
            .Be(900, "500 included, plus the 400 the previous window left unused");
    }

    [Fact]
    public async Task An_opened_window_is_still_held_to_the_allowance_it_opened_with()
    {
        // The counter's snapshot wins over anything recomputed, so repairing the previous window or
        // editing the plan cannot move an allowance a customer is already spending against.
        GivenCarryForward(cap: null);
        GivenPreviousWindow(balance: 100, limitSnapshot: 500);
        GivenCurrentWindow(balance: 10, limitSnapshot: 640);

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements
            .Single(entitlement => entitlement.Key == "pep_screening")
            .Limit
            .Should()
            .Be(640, "frozen when the window opened");
    }

    [Fact]
    public async Task A_carry_forward_cap_bounds_what_is_advertised()
    {
        GivenCarryForward(cap: 50);
        GivenPreviousWindow(balance: 100, limitSnapshot: 500);

        var result = await Service().GetAsync(false, null, "corr-1", CancellationToken.None);

        result.Value!.Entitlements
            .Single(entitlement => entitlement.Key == "pep_screening")
            .Limit
            .Should()
            .Be(550, "400 went unused but the plan carries at most 50");
    }

    private void GivenCarryForward(long? cap)
    {
        _subscription!.Plan.Meters[0].ResetPolicy = MeterResetPolicy.CarryForward;
        _subscription.Plan.Meters[0].CarryForwardCap = cap;
    }

    /// <summary>The window before the one the clock sits in, addressed by its own counter id.</summary>
    private void GivenPreviousWindow(long balance, long limitSnapshot) =>
        GivenCounter("20260701", balance, limitSnapshot);

    private void GivenCurrentWindow(long balance, long limitSnapshot) =>
        GivenCounter("20260801", balance, limitSnapshot);

    private void GivenCounter(string periodKeyFragment, long balance, long limitSnapshot) =>
        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId,
                It.Is<string>(id => id.Contains(periodKeyFragment, StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter
            {
                Balance = balance,
                LimitSnapshot = limitSnapshot
            });

    private EntitlementService Service() => new(
        _subscriptions.Object,
        _usage.Object,
        new MeterAllowanceResolver(_usage.Object),
        _contextResolver.Object,
        new EntitlementSnapshotCache(new OptionsStub(), _time),
        _time);

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        CurrentPeriodEndUtc = new DateTime(2026, 8, 31, 21, 59, 59, DateTimeKind.Utc),
        QuantityItems =
        [
            new SubscriptionQuantityItem
            {
                ItemKey = "seat",
                UnitLabel = "seat",
                Quantity = 12
            }
        ],
        Plan = new PlanSnapshot
        {
            Code = "professional",
            Entitlements =
            [
                new PlanEntitlement
                {
                    Key = "pep_screening",
                    LimitKind = EntitlementLimitKind.Count,
                    Limit = 500,
                    MeterKey = "screening",
                    UnitLabel = "screening"
                }
            ],
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "screening",
                    UnitLabel = "screening",
                    IncludedQuantity = 500
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
