using FluentAssertions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription.Credits;

/// <summary>
/// When credit grants stop being spendable: expiry, plan changes and the end of the subscription
/// (spec 002, phase D).
/// </summary>
/// <remarks>
/// Guards against credits outliving what they were sold with — carry-forward credits surviving a
/// plan change, credits spent on a cancelled subscription — and against the opposite mistake of
/// taking away credits a renewal or an unpaid invoice should not touch.
/// </remarks>
public sealed class CreditLifecycleTests
{
    private const string TenantId = "tenant-1";
    private const string SubscriptionId = "sub-1";

    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PlanChangedAt = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryCreditGrants _grants = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly SubscriptionDetail _subscription = Subscription();

    public CreditLifecycleTests() =>
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

    [Fact]
    public void Credits_that_expire_with_the_window_lapse_when_it_ends()
    {
        var grant = Grant(MeterResetPolicy.Periodic, grantedAt: Now.AddDays(-2), expiresAt: Now);

        CreditLedger.EffectiveStatus(grant, _subscription, Now).Should().Be(CreditGrantStatus.Expired, "AC-14");
    }

    [Fact]
    public void Carry_forward_credits_survive_a_renewal()
    {
        var grant = Grant(MeterResetPolicy.CarryForward, grantedAt: PlanChangedAt.AddDays(1));

        CreditLedger.EffectiveStatus(grant, _subscription, Now.AddMonths(2)).Should().Be(CreditGrantStatus.Active,
            "a renewal does not re-anchor the usage schedule, so the grant is not before it (AC-15)");
    }

    [Fact]
    public void Carry_forward_credits_are_forfeited_by_a_plan_change()
    {
        var grant = Grant(MeterResetPolicy.CarryForward, grantedAt: PlanChangedAt.AddDays(-1));

        CreditLedger.EffectiveStatus(grant, _subscription, Now).Should().Be(CreditGrantStatus.Forfeited,
            "the plan change re-anchored the usage schedule after these were granted (AC-16)");
    }

    [Fact]
    public void Never_expiring_credits_survive_a_plan_change_that_keeps_their_meter()
    {
        var grant = Grant(MeterResetPolicy.Never, grantedAt: PlanChangedAt.AddDays(-1));

        CreditLedger.EffectiveStatus(grant, _subscription, Now).Should().Be(CreditGrantStatus.Active, "AC-17");
    }

    [Fact]
    public void Credits_are_forfeited_when_the_plan_no_longer_has_their_meter()
    {
        var grant = Grant(MeterResetPolicy.Never, grantedAt: PlanChangedAt.AddDays(-1));
        _subscription.Plan.Meters.Clear();

        CreditLedger.EffectiveStatus(grant, _subscription, Now).Should().Be(CreditGrantStatus.Forfeited, "AC-17");
    }

    [Theory]
    [InlineData(SubscriptionStatus.Canceled, CreditGrantStatus.Forfeited)]
    [InlineData(SubscriptionStatus.IncompleteExpired, CreditGrantStatus.Forfeited)]
    [InlineData(SubscriptionStatus.Unpaid, CreditGrantStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue, CreditGrantStatus.Active)]
    public void Credits_end_with_the_subscription_but_not_with_a_missed_payment(
        SubscriptionStatus status,
        CreditGrantStatus expected)
    {
        _subscription.Status = status;

        CreditLedger.EffectiveStatus(Grant(MeterResetPolicy.Never), _subscription, Now).Should().Be(expected,
            "an ended subscription forfeits its credits, and an unpaid one can still be paid and resumed (AC-18)");
    }

    [Fact]
    public async Task Forfeited_credits_are_refused_before_the_sweep_has_marked_them()
    {
        _grants.All.Add(Grant(MeterResetPolicy.CarryForward, grantedAt: PlanChangedAt.AddDays(-1)));

        var draw = await Ledger().DrawAsync(_subscription, "ai-credit", null, 5, Now, CancellationToken.None);

        draw.Drawn.Should().Be(0, "spending must not wait a day for the maintenance sweep to catch up");
    }

    [Fact]
    public async Task The_maintenance_sweep_closes_lapsed_grants_and_stamps_the_rest()
    {
        var expired = Grant(MeterResetPolicy.Periodic, "expired", expiresAt: Now.AddHours(-1));
        var forfeited = Grant(MeterResetPolicy.CarryForward, "forfeited", grantedAt: PlanChangedAt.AddDays(-1));
        var live = Grant(MeterResetPolicy.Never, "live");
        _grants.All.AddRange([expired, forfeited, live]);

        var closed = await Ledger().MaintainAsync(TenantId, Now, TimeSpan.FromHours(24), 100, CancellationToken.None);

        closed.Should().Be(2);
        expired.Status.Should().Be(CreditGrantStatus.Expired);
        forfeited.Status.Should().Be(CreditGrantStatus.Forfeited);
        live.Status.Should().Be(CreditGrantStatus.Active);
        live.LastCheckedAtUtc.Should().Be(Now, "a grant just checked is not due again until the interval passes");
    }

    [Fact]
    public async Task Every_grant_is_forfeited_once_its_subscription_is_gone()
    {
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);
        _grants.All.Add(Grant(MeterResetPolicy.Never, "orphan"));

        await Ledger().MaintainAsync(TenantId, Now, TimeSpan.FromHours(24), 100, CancellationToken.None);

        _grants.All.Single().Status.Should().Be(CreditGrantStatus.Forfeited);
    }

    private CreditLedger Ledger() => new(
        _grants,
        new Mock<ISubscriptionUsageRepository>().Object,
        _subscriptions.Object,
        new Mock<ISubscriptionOutboxEventFactory>().Object,
        new Mock<ISubscriptionWorkScheduler>().Object);

    private static SubscriptionCreditGrant Grant(
        MeterResetPolicy policy,
        string id = "g1",
        DateTime? grantedAt = null,
        DateTime? expiresAt = null) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        SubscriptionId = SubscriptionId,
        MeterKey = "ai-credit",
        Quantity = 10,
        Remaining = 10,
        ExpiryPolicy = policy,
        ExpiresAtUtc = expiresAt,
        GrantedAtUtc = grantedAt ?? PlanChangedAt.AddDays(1),
        Status = CreditGrantStatus.Active
    };

    private static SubscriptionDetail Subscription() => new()
    {
        ItemId = SubscriptionId,
        TenantId = TenantId,
        Status = SubscriptionStatus.Active,
        // Re-anchored by the last plan change, which is how a carry-forward grant from before it is told.
        UsageSchedule = new BillingSchedule { AnchorInstantUtc = PlanChangedAt, TimeZoneId = "UTC" },
        Plan = new PlanSnapshot { Meters = [new PlanMeter { MeterKey = "ai-credit" }] }
    };
}
