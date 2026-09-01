using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// That an opening-stub upgrade's replacement year survives the database, and that a crash between
/// the charge and the promotion cannot leave a different year behind than a clean run would.
/// </summary>
/// <remarks>
/// Unit tests prove the services pass the right object; they cannot prove Mongo stores it. The
/// replacement year travels as a nested document inside a settlement reservation and is installed
/// by an update definition that names one field — two places a mistake is invisible to a mocked
/// repository and total in production: a reservation that round-trips without its replacement year
/// promotes an upgrade that silently keeps the old annual figures, and money has already moved by
/// the time anyone could notice.
/// <para>
/// Driven through the real <see cref="SubscriptionRepository"/> against a real database, because
/// the invariants under test belong to the persistence layer itself — the serializer's treatment of
/// a nullable nested entity, and whether the promotion's <c>$set</c> lands atomically with the plan
/// it belongs to.
/// </para>
/// <para>
/// The crash is simulated the way the renewal recovery suite simulates one: by the state a crash
/// leaves behind rather than by killing a process. The reservation is written and the promotion is
/// then replayed against it, which is exactly the position a worker that died after charging
/// leaves the subscription in.
/// </para>
/// </remarks>
public sealed class OpeningStubUpgradeRecoveryIntegrationTests
    : IClassFixture<MongoIntegrationFixture>
{
    private const string OrganizationId = "org-1";

    private static readonly DateTime StubStartUtc = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AnnualStartUtc = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AnnualEndUtc = new(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly MongoIntegrationFixture _fixture;
    private readonly string _tenantId = MongoIntegrationFixture.NewTenantId();

    // Unique per test: the item id is the document's _id and the class fixture shares one database
    // across the file, so a reused id inserts a duplicate and silently reads back nothing.
    private readonly string _subscriptionId = Guid.NewGuid().ToString("N");
    private readonly string _reservationId = Guid.NewGuid().ToString("N");

    public OpeningStubUpgradeRecoveryIntegrationTests(MongoIntegrationFixture fixture) =>
        _fixture = fixture;

    /// <summary>
    /// A reserved plan change carries its replacement year through the database intact.
    /// </summary>
    /// <remarks>
    /// The first half of the recovery guarantee: whatever the sweep later promotes, it can only be
    /// as good as what the reservation actually stored. A nested entity that the serializer drops
    /// would leave the replay with nothing to install and no way to know it was ever meant to.
    /// </remarks>
    [Fact]
    public async Task A_reserved_plan_change_stores_its_replacement_year()
    {
        var subscriptions = new SubscriptionRepository(_fixture.DbContextProvider);
        var subscription = await GivenSubscriptionAsync(subscriptions);

        await subscriptions.TryReserveSettlementAsync(
            _tenantId, _subscriptionId, subscription.Version, PlanReservation(), default);

        var reserved = (await Read(subscriptions)).SettlementReservation;

        reserved.Should().NotBeNull();
        var replacement = reserved!.PlanChange!.ReplacementPendingAnnualPeriod;
        replacement.Should().NotBeNull("a dropped replacement year cannot be recovered from");
        replacement!.AmountMinor.Should().Be(1_200_000);
        replacement.GrossAmountMinor.Should().Be(1_500_000);
        replacement.PromotionalDiscountMinor.Should().Be(300_000);
        replacement.DiscountApplied.Should().BeTrue();
        replacement.IsPrepaid.Should().BeTrue();
        replacement.StartUtc.Should().Be(AnnualStartUtc);
        replacement.EndUtc.Should().Be(AnnualEndUtc);
        replacement.PaymentDetailId.Should().Be("pay-original");
    }

    /// <summary>
    /// Promoting the reservation installs the plan and the replacement year in one write, and
    /// releases the reservation as it does.
    /// </summary>
    [Fact]
    public async Task Promoting_a_plan_change_installs_the_replacement_year_atomically()
    {
        var subscriptions = new SubscriptionRepository(_fixture.DbContextProvider);
        var subscription = await GivenSubscriptionAsync(subscriptions);
        var reservation = PlanReservation();

        await subscriptions.TryReserveSettlementAsync(
            _tenantId, _subscriptionId, subscription.Version, reservation, default);

        var promoted = await PromotePlanAsync(subscriptions, reservation, "pay-adjustment");

        promoted.Should().BeTrue();

        var settled = await Read(subscriptions);

        settled.Plan.Code.Should().Be("scale");
        settled.PendingAnnualPeriod.Should().NotBeNull();
        settled.PendingAnnualPeriod!.AmountMinor.Should().Be(1_200_000);
        settled.PendingAnnualPeriod.StartUtc.Should().Be(AnnualStartUtc);
        settled.PendingAnnualPeriod.EndUtc.Should().Be(AnnualEndUtc);

        // The adjustment's payment, stamped at promotion — the value the request path installs for
        // this same reservation and charge.
        settled.PendingAnnualPeriod.PaymentDetailId.Should().Be("pay-adjustment");

        // Same write: a promotion that installed the plan but left the reservation standing would
        // block every later change on a subscription that has already been settled.
        settled.SettlementReservation.Should().BeNull();
        settled.CreditBalanceMinor.Should().Be(0);
    }

    /// <summary>
    /// Replaying a promotion that already landed changes nothing and reports that it changed
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The sweep is at-least-once, so a reservation can be replayed after a successful promotion
    /// whose acknowledgement was lost. The promotion is addressed by the reservation id and that id
    /// is cleared by the write, so the second attempt matches no document — which is what makes the
    /// year installed exactly once rather than re-stamped by whichever replay ran last.
    /// </remarks>
    [Fact]
    public async Task A_replayed_promotion_neither_reapplies_nor_rewrites_the_year()
    {
        var subscriptions = new SubscriptionRepository(_fixture.DbContextProvider);
        var subscription = await GivenSubscriptionAsync(subscriptions);
        var reservation = PlanReservation();

        await subscriptions.TryReserveSettlementAsync(
            _tenantId, _subscriptionId, subscription.Version, reservation, default);

        var first = await PromotePlanAsync(subscriptions, reservation, "pay-adjustment");
        var afterFirst = await Read(subscriptions);

        // A replay carrying a different payment, which is the case that would corrupt the record if
        // the reservation id did not gate the write.
        var second = await PromotePlanAsync(subscriptions, reservation, "pay-duplicate");
        var afterSecond = await Read(subscriptions);

        first.Should().BeTrue();
        second.Should().BeFalse("the reservation was cleared by the promotion that already landed");

        afterSecond.PendingAnnualPeriod!.PaymentDetailId.Should().Be("pay-adjustment");
        afterSecond.Version.Should().Be(afterFirst.Version, "the replay wrote nothing");
    }

    /// <summary>
    /// A promotion that loses its race leaves the reservation exactly as it found it.
    /// </summary>
    /// <remarks>
    /// The failure this rules out is a partial one: a write that installed some of the year, or
    /// cleared the reservation without installing any of it, would strand a paid-for upgrade with
    /// nothing left to replay it from. A promotion addressed to a reservation this subscription is
    /// not holding must be a no-op in every field it touches.
    /// </remarks>
    [Fact]
    public async Task A_promotion_for_another_reservation_leaves_this_one_untouched()
    {
        var subscriptions = new SubscriptionRepository(_fixture.DbContextProvider);
        var subscription = await GivenSubscriptionAsync(subscriptions);
        var reservation = PlanReservation();

        await subscriptions.TryReserveSettlementAsync(
            _tenantId, _subscriptionId, subscription.Version, reservation, default);

        var stale = PlanReservation();
        stale.ReservationId = Guid.NewGuid().ToString("N");

        var promoted = await PromotePlanAsync(subscriptions, stale, "pay-wrong");

        promoted.Should().BeFalse();

        var untouched = await Read(subscriptions);

        untouched.Plan.Code.Should().Be("team", "the plan must not move for another reservation");
        untouched.PendingAnnualPeriod!.AmountMinor.Should().Be(
            1_000_000, "the year on the subscription is still the one that was bought");
        untouched.PendingAnnualPeriod.PaymentDetailId.Should().Be("pay-original");
        untouched.SettlementReservation.Should().NotBeNull("the real reservation is still owed");
        untouched.SettlementReservation!.ReservationId.Should().Be(_reservationId);
        untouched.SettlementReservation.PlanChange!.ReplacementPendingAnnualPeriod
            .Should().NotBeNull("a replay must still have everything it needs");
    }

    /// <summary>
    /// The same guarantees for a quantity increase, which reaches the year through its own
    /// reservation payload and its own promotion.
    /// </summary>
    [Fact]
    public async Task Promoting_a_quantity_increase_installs_the_replacement_year_atomically()
    {
        var subscriptions = new SubscriptionRepository(_fixture.DbContextProvider);
        var subscription = await GivenSubscriptionAsync(subscriptions);
        var reservation = QuantityReservation();

        await subscriptions.TryReserveSettlementAsync(
            _tenantId, _subscriptionId, subscription.Version, reservation, default);

        var reserved = (await Read(subscriptions)).SettlementReservation;
        reserved!.QuantityChange!.ReplacementPendingAnnualPeriod
            .Should().NotBeNull("the quantity payload carries the year too");

        var promoted = await subscriptions.TryPromoteQuantityReservationAsync(
            _tenantId,
            _subscriptionId,
            reservation.ReservationId,
            reservation.QuantityChange!.RequestedQuantities,
            reservation.QuantityChange.NewCreditBalanceMinor,
            "pay-adjustment",
            OutboxEvent(),
            default,
            reserved.QuantityChange.ReplacementPendingAnnualPeriod!.SettledBy("pay-adjustment"));

        promoted.Should().BeTrue();

        var settled = await Read(subscriptions);

        settled.QuantityItems.Single().Quantity.Should().Be(12);
        settled.PendingAnnualPeriod!.AmountMinor.Should().Be(1_200_000);
        settled.PendingAnnualPeriod.PaymentDetailId.Should().Be("pay-adjustment");
        settled.PendingAnnualPeriod.StartUtc.Should().Be(AnnualStartUtc);
        settled.SettlementReservation.Should().BeNull();
    }

    /// <summary>
    /// An ordinary plan change — one with no year to replace — leaves the year alone rather than
    /// clearing it.
    /// </summary>
    /// <remarks>
    /// The omitted-parameter case, and the reason the update is conditional rather than an
    /// unconditional <c>$set</c> of a nullable field. An unconditional set would erase a prepaid
    /// year on every ordinary change that happened to run while one was pending, which is a year
    /// the subscriber has paid for and nothing would restore.
    /// </remarks>
    [Fact]
    public async Task A_change_carrying_no_replacement_year_leaves_the_existing_one_standing()
    {
        var subscriptions = new SubscriptionRepository(_fixture.DbContextProvider);
        var subscription = await GivenSubscriptionAsync(subscriptions);

        var applied = await subscriptions.TryChangePlanAsync(
            _tenantId,
            _subscriptionId,
            subscription.Version,
            null,
            new PlanSnapshot { Code = "scale", DisplayName = "Scale" },
            new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 1_200_000 },
            [Quantity(10)],
            Schedule(),
            new PendingUsagePeriod(),
            0,
            null,
            OutboxEvent(),
            default);

        applied.Should().BeTrue();

        var settled = await Read(subscriptions);

        settled.Plan.Code.Should().Be("scale");
        settled.PendingAnnualPeriod.Should().NotBeNull(
            "a change with no year of its own must never clear one the subscriber has paid for");
        settled.PendingAnnualPeriod!.AmountMinor.Should().Be(1_000_000);
        settled.PendingAnnualPeriod.PaymentDetailId.Should().Be("pay-original");
    }

    private Task<bool> PromotePlanAsync(
        ISubscriptionRepository subscriptions,
        SettlementReservation reservation,
        string paymentDetailId) =>
        subscriptions.TryChangePlanAsync(
            _tenantId,
            _subscriptionId,
            // Deliberately stale: a promotion is addressed by its reservation, never by a version,
            // because the money has already moved and a concurrent bump must not strand it.
            expectedVersion: 0,
            reservation.ReservationId,
            reservation.PlanChange!.Plan,
            reservation.PlanChange.Price,
            reservation.PlanChange.QuantityItems,
            reservation.PlanChange.Schedule,
            reservation.PlanChange.OutgoingUsagePeriod,
            reservation.PlanChange.NewCreditBalanceMinor,
            paymentDetailId,
            OutboxEvent(),
            default,
            replacementPendingAnnualPeriod:
                reservation.PlanChange.ReplacementPendingAnnualPeriod?.SettledBy(paymentDetailId));

    private async Task<SubscriptionDetail> GivenSubscriptionAsync(
        ISubscriptionRepository subscriptions)
    {
        await subscriptions.TryCreateAsync(NewSubscription(), default);

        return await Read(subscriptions);
    }

    private async Task<SubscriptionDetail> Read(ISubscriptionRepository subscriptions) =>
        (await subscriptions.GetAsync(_tenantId, OrganizationId, _subscriptionId, default))!;

    private SettlementReservation PlanReservation() => new()
    {
        ReservationId = _reservationId,
        Kind = SettlementReservationKind.PlanChange,
        ChargeAmountMinor = 260_000,
        BillingAccountId = "acct-1",
        ProviderName = "STRIPE",
        ProviderCustomerId = "cus_123",
        StoredPaymentMethodId = "pm-1",
        ReservedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc),
        CorrelationId = "corr-1",
        ReservedAtVersion = 1,
        PlanChange = new ReservedPlanChange
        {
            Plan = new PlanSnapshot { Code = "scale", DisplayName = "Scale" },
            Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 1_500_000 },
            QuantityItems = [Quantity(10)],
            Schedule = Schedule(),
            OutgoingUsagePeriod = new PendingUsagePeriod(),
            NewCreditBalanceMinor = 0,
            ReplacementPendingAnnualPeriod = ReplacementAnnual()
        }
    };

    private SettlementReservation QuantityReservation() => new()
    {
        ReservationId = _reservationId,
        Kind = SettlementReservationKind.QuantityIncrease,
        ChargeAmountMinor = 260_000,
        BillingAccountId = "acct-1",
        ProviderName = "STRIPE",
        ProviderCustomerId = "cus_123",
        StoredPaymentMethodId = "pm-1",
        ReservedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc),
        CorrelationId = "corr-1",
        ReservedAtVersion = 1,
        QuantityChange = new ReservedQuantityChange
        {
            RequestedQuantities = [Quantity(12)],
            NewCreditBalanceMinor = 0,
            ReplacementPendingAnnualPeriod = ReplacementAnnual()
        }
    };

    /// <summary>
    /// The year as reserved: repriced onto the target's terms, and still naming the payment that
    /// bought the terms being replaced — the adjustment's own payment does not exist yet when a
    /// reservation is written.
    /// </summary>
    private static PendingAnnualPeriod ReplacementAnnual() => new()
    {
        StartUtc = AnnualStartUtc,
        EndUtc = AnnualEndUtc,
        GrossAmountMinor = 1_500_000,
        PromotionalDiscountMinor = 300_000,
        AmountMinor = 1_200_000,
        NetAmountMinor = 1_200_000,
        DiscountApplied = true,
        CollectedWithCheckout = true,
        IsPrepaid = true,
        PaymentDetailId = "pay-original"
    };

    private static SubscriptionPlanSchedule Schedule() => new(
        MonthlySchedule(),
        StubStartUtc,
        AnnualStartUtc,
        AnnualStartUtc,
        MonthlySchedule(),
        StubStartUtc,
        AnnualStartUtc,
        AnnualStartUtc);

    private static BillingSchedule MonthlySchedule() => new()
    {
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        TimeZoneId = "UTC",
        AnchorDayOfMonth = 1
    };

    private static SubscriptionQuantityItem Quantity(long quantity) => new()
    {
        ItemKey = "user",
        UnitLabel = "user",
        Quantity = quantity,
        UnitAmountMinor = 100_000
    };

    private static SubscriptionOutboxEvent OutboxEvent() =>
        new SubscriptionOutboxEventFactory().CreateQuantityChanged(
            new SubscriptionDetail
            {
                ItemId = "sub-1",
                TenantId = "tenant-1",
                OrganizationId = OrganizationId,
                Plan = new PlanSnapshot { Code = "scale" },
                Price = new PriceSnapshot { CurrencyCode = "CHF" }
            },
            "corr-1");

    /// <summary>
    /// A subscriber part-way through a prepaid opening stub: on a calendar-aligned yearly price,
    /// inside the days before the year opens, with that year already paid for.
    /// </summary>
    private SubscriptionDetail NewSubscription() => new()
    {
        ItemId = _subscriptionId,
        TenantId = _tenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Version = 1,
        CurrentPeriodStartUtc = StubStartUtc,
        CurrentPeriodEndUtc = AnnualStartUtc,
        NextFeeBillingAtUtc = AnnualStartUtc,
        Plan = new PlanSnapshot { Code = "team", DisplayName = "Team" },
        Price = new PriceSnapshot
        {
            CurrencyCode = "CHF",
            UnitAmountMinor = 1_000_000,
            Interval = BillingInterval.Year,
            IntervalCount = 1,
            BillingAlignment = BillingAlignment.CalendarMonth,
            CalendarStubBasePriceId = "price-monthly",
            CalendarStubBaseUnitAmountMinor = 90_000
        },
        QuantityItems = [Quantity(10)],
        FeeSchedule = MonthlySchedule(),
        UsageSchedule = MonthlySchedule(),
        PendingAnnualPeriod = new PendingAnnualPeriod
        {
            StartUtc = AnnualStartUtc,
            EndUtc = AnnualEndUtc,
            GrossAmountMinor = 1_250_000,
            PromotionalDiscountMinor = 250_000,
            AmountMinor = 1_000_000,
            NetAmountMinor = 1_000_000,
            DiscountApplied = true,
            CollectedWithCheckout = true,
            IsPrepaid = true,
            PaymentDetailId = "pay-original"
        }
    };
}
