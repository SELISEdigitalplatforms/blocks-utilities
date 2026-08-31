using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Integration;

/// <summary>
/// The parts of scheduling a plan change that only a real database can demonstrate.
/// </summary>
/// <remarks>
/// Three of these are guarantees MongoDB enforces rather than our code: the compound filter that
/// refuses to book a plan change over a quantity change, the compare-and-set behind replacing and
/// cancelling one, and the transition that installs a due change. A mock would answer whatever it
/// was told and every one of them would pass while the real collection happily held two pending
/// changes at once.
/// <para>
/// The fourth is the newest and least obvious: <see cref="SubscriptionTransition"/> grew Plan,
/// Price, FeeSchedule, UsageSchedule and ClearPendingPlanChange for the renewal to install a
/// scheduled change, and nothing outside this file proves those fields actually reach the
/// document. A renewal that silently dropped the price would bill the new plan at the old rate.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class PendingPlanChangeIntegrationTests
{
    private readonly SubscriptionRepository _subscriptions;

    public PendingPlanChangeIntegrationTests(MongoIntegrationFixture fixture) =>
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);

    /// <summary>
    /// One pending commercial change at a time, proven against the race rather than the check.
    /// </summary>
    /// <remarks>
    /// Both services refuse the other's pending change by name before writing, but that check and
    /// the write are two operations: two callers can both pass it. The filter on the write is what
    /// actually holds, and this is the only way to show it.
    /// </remarks>
    [Fact]
    public async Task A_plan_change_is_never_scheduled_over_a_quantity_change()
    {
        var (tenantId, subscription) = await GivenSubscriptionAsync();

        (await _subscriptions.TrySetPendingQuantityChangeAsync(
            tenantId, subscription.ItemId, subscription.Version,
            new PendingQuantityChange
            {
                RequestedQuantities = [],
                EffectiveAtUtc = DateTime.UtcNow.AddDays(30)
            },
            CancellationToken.None)).Should().BeTrue();

        var scheduled = await _subscriptions.TrySetPendingPlanChangeAsync(
            tenantId, subscription.ItemId, subscription.Version + 1,
            NewPendingPlanChange(), CancellationToken.None);

        scheduled.Should().BeFalse(
            "a quantity change is already booked for the period the next renewal charges for");

        var stored = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);
        stored!.PendingPlanChange.Should().BeNull();
        stored.PendingQuantityChange.Should().NotBeNull("the change already booked survives");
    }

    [Fact]
    public async Task Concurrent_plan_and_quantity_scheduling_leaves_exactly_one_booked()
    {
        var (tenantId, subscription) = await GivenSubscriptionAsync();

        // Both against the same version, started before either is awaited: the shape two tabs
        // confirming at once actually takes.
        var planTask = _subscriptions.TrySetPendingPlanChangeAsync(
            tenantId, subscription.ItemId, subscription.Version,
            NewPendingPlanChange(), CancellationToken.None);
        var quantityTask = _subscriptions.TrySetPendingQuantityChangeAsync(
            tenantId, subscription.ItemId, subscription.Version,
            new PendingQuantityChange
            {
                RequestedQuantities = [],
                EffectiveAtUtc = DateTime.UtcNow.AddDays(30)
            },
            CancellationToken.None);

        var outcomes = await Task.WhenAll(planTask, quantityTask);

        outcomes.Count(won => won).Should().Be(1, "the version they share admits exactly one write");

        var stored = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);
        (stored!.PendingPlanChange is not null && stored.PendingQuantityChange is not null)
            .Should().BeFalse("no subscription ever holds both");
    }

    [Fact]
    public async Task A_second_plan_change_replaces_the_one_already_booked()
    {
        var (tenantId, subscription) = await GivenSubscriptionAsync();

        (await _subscriptions.TrySetPendingPlanChangeAsync(
            tenantId, subscription.ItemId, subscription.Version,
            NewPendingPlanChange("first"), CancellationToken.None)).Should().BeTrue();

        (await _subscriptions.TrySetPendingPlanChangeAsync(
            tenantId, subscription.ItemId, subscription.Version + 1,
            NewPendingPlanChange("second"), CancellationToken.None)).Should().BeTrue();

        var stored = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);
        stored!.PendingPlanChange!.Plan.Code.Should().Be(
            "second", "a customer changing their mind replaces rather than queues");
    }

    [Fact]
    public async Task Cancelling_a_scheduled_change_against_a_stale_version_is_refused()
    {
        var (tenantId, subscription) = await GivenSubscriptionAsync();

        (await _subscriptions.TrySetPendingPlanChangeAsync(
            tenantId, subscription.ItemId, subscription.Version,
            NewPendingPlanChange(), CancellationToken.None)).Should().BeTrue();

        // The version the caller read before the booking above moved it on.
        (await _subscriptions.TryClearPendingPlanChangeAsync(
            tenantId, subscription.ItemId, subscription.Version,
            CancellationToken.None)).Should().BeFalse();

        var stillBooked = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);
        stillBooked!.PendingPlanChange.Should().NotBeNull();

        (await _subscriptions.TryClearPendingPlanChangeAsync(
            tenantId, subscription.ItemId, subscription.Version + 1,
            CancellationToken.None)).Should().BeTrue();

        var cleared = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);
        cleared!.PendingPlanChange.Should().BeNull();
    }

    /// <summary>
    /// The transition that installs a due change actually writes every part of it.
    /// </summary>
    /// <remarks>
    /// All five fields together, because the failure modes are individually silent: a plan
    /// installed without its price bills the new plan at the old rate, and a schedule that is not
    /// cleared installs the same change again next period.
    /// </remarks>
    [Fact]
    public async Task Applying_a_due_change_installs_the_plan_price_and_schedules_and_clears_it()
    {
        var (tenantId, subscription) = await GivenSubscriptionAsync();

        (await _subscriptions.TrySetPendingPlanChangeAsync(
            tenantId, subscription.ItemId, subscription.Version,
            NewPendingPlanChange(), CancellationToken.None)).Should().BeTrue();

        var pending = NewPendingPlanChange();
        var applied = await _subscriptions.TryTransitionAsync(
            tenantId,
            subscription.ItemId,
            new SubscriptionTransition(SubscriptionStatus.Active, SubscriptionStatus.Active)
            {
                Plan = pending.Plan,
                Price = pending.Price,
                FeeSchedule = pending.FeeSchedule,
                UsageSchedule = pending.UsageSchedule,
                ClearPendingPlanChange = true,
                CurrentPeriodStartUtc = pending.EffectiveAtUtc,
                CurrentPeriodEndUtc = pending.EffectiveAtUtc.AddYears(1)
            },
            CancellationToken.None);

        applied.Should().BeTrue();

        var stored = await _subscriptions.GetByIdAsync(
            tenantId, subscription.ItemId, CancellationToken.None);

        stored!.Plan.Code.Should().Be("premium");
        stored.Price.UnitAmountMinor.Should().Be(19_900);
        stored.FeeSchedule.Interval.Should().Be(BillingInterval.Year);
        stored.UsageSchedule.Interval.Should().Be(BillingInterval.Month);
        stored.PendingPlanChange.Should().BeNull("the schedule is forgotten in the same write");

        // And the period it opened is the annual one, not another month.
        (stored.CurrentPeriodEndUtc - stored.CurrentPeriodStartUtc)
            .Should().BeCloseTo(TimeSpan.FromDays(365), TimeSpan.FromDays(1));
    }

    private async Task<(string TenantId, SubscriptionDetail Subscription)> GivenSubscriptionAsync()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var id = Guid.NewGuid().ToString();
        var subscription = new SubscriptionDetail
        {
            ItemId = id,
            TenantId = tenantId,
            OrganizationId = "org-1",
            BillingAccountId = "acct-1",
            Status = SubscriptionStatus.Active,
            CurrencyCode = "CHF",
            OrderId = $"sub:{id}",
            Plan = new PlanSnapshot { Code = "basic", DisplayName = "Basic" },
            Price = new PriceSnapshot
            {
                CurrencyCode = "CHF",
                UnitAmountMinor = 8_900,
                Interval = BillingInterval.Month,
                IntervalCount = 1
            }
        };

        (await _subscriptions.TryCreateAsync(subscription, CancellationToken.None))
            .Should().BeTrue();

        var stored = await _subscriptions.GetByIdAsync(tenantId, id, CancellationToken.None);

        return (tenantId, stored!);
    }

    private static PendingPlanChange NewPendingPlanChange(string planCode = "premium")
    {
        var effectiveAtUtc = new DateTime(2027, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        return new PendingPlanChange
        {
            Plan = new PlanSnapshot { Code = planCode, DisplayName = "Premium" },
            Price = new PriceSnapshot
            {
                CurrencyCode = "CHF",
                UnitAmountMinor = 19_900,
                Interval = BillingInterval.Year,
                IntervalCount = 1
            },
            QuantityItems = [],
            // Anchored on the date the change becomes real, which is the whole point of freezing
            // it rather than deriving it when the boundary arrives.
            FeeSchedule = new BillingSchedule
            {
                Interval = BillingInterval.Year,
                IntervalCount = 1,
                AnchorInstantUtc = effectiveAtUtc,
                TimeZoneId = "UTC",
                AnchorDayOfMonth = 1
            },
            UsageSchedule = new BillingSchedule
            {
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                AnchorInstantUtc = effectiveAtUtc,
                TimeZoneId = "UTC",
                AnchorDayOfMonth = 1
            },
            RequestedAtUtc = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
            EffectiveAtUtc = effectiveAtUtc,
            ExpectedVersion = 0
        };
    }
}
