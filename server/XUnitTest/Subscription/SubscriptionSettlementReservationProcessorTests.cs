using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Finishing quantity increases whose caller died between reserving the units and settling the
/// charge.
/// </summary>
/// <remarks>
/// The reservation exists so neither half can be lost. These are the cases that prove it: money
/// taken and units owed — under either name a gateway records it by — money never taken and a
/// reservation to give back, and a charge nobody can answer for, which must be held rather than
/// guessed at in either direction.
/// </remarks>
public sealed class SubscriptionSettlementReservationProcessorTests
{
    private const string TenantId = "tenant-1";
    private const string ReservationId = "reservation-1";

    private static readonly string ChargeKey =
        SubscriptionConstants.SettlementChargeKeyFor("sub-1", ReservationId);

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

    private readonly SubscriptionDetail _subscription = new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = "org-1",
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Active,
        Version = 7,
        CurrencyCode = "CHF",
        QuantityItems = [Item(4)],
        Plan = new PlanSnapshot { Code = "team", DisplayName = "Team" },
        SettlementReservation = new SettlementReservation
        {
            ReservationId = ReservationId,
            Kind = SettlementReservationKind.QuantityIncrease,
            QuantityChange = new ReservedQuantityChange
            {
                RequestedQuantities = [Item(5)],
                NewCreditBalanceMinor = 0
            },
            ChargeAmountMinor = 5_437,
            BillingAccountId = "acct-1",
            ProviderName = "STRIPE",
            ProviderOrganizationId = "org-merchant",
            ProviderCustomerId = "cus_123",
            StoredPaymentMethodId = "pm-1",
            ReservedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc),
            CorrelationId = "corr-1"
        }
    };

    public SubscriptionSettlementReservationProcessorTests()
    {
        _subscriptions
            .Setup(repository => repository.ListStaleSettlementsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [_subscription]);

        _subscriptions
            .Setup(repository => repository.TryPromoteQuantityReservationAsync(
                TenantId, "sub-1", ReservationId,
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryReleaseSettlementAsync(
                TenantId, "sub-1", ReservationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-2", "corr-1"));
    }

    [Theory]
    [InlineData(PaymentStatuses.Captured)]
    [InlineData(PaymentStatuses.Authorized)]
    [InlineData(PaymentStatuses.PartiallyCaptured)]
    public async Task A_settled_charge_grants_the_units_it_paid_for(string status)
    {
        GivenPayment(ChargeKey, status);

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(1);
        _subscriptions.Verify(
            repository => repository.TryPromoteQuantityReservationAsync(
                TenantId, "sub-1", ReservationId,
                It.Is<List<SubscriptionQuantityItem>>(items => items.Single().Quantity == 5),
                0, "pay-1", It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_charge_recorded_under_the_settlement_key_is_still_found()
    {
        // An invoice that was already paid when it was finalized is recorded under the settlement
        // key, not the key the attempt reserved. Looking under only the reserved key released a
        // reservation the subscriber had paid for.
        GivenPayment(
            SubscriptionConstants.RecordedSettlementKeyFor(ChargeKey),
            PaymentStatuses.Captured);

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(1);
        _subscriptions.Verify(
            repository => repository.TryPromoteQuantityReservationAsync(
                TenantId, "sub-1", ReservationId,
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), "pay-1",
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _subscriptions.Verify(
            repository => repository.TryReleaseSettlementAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "the money moved, so the reservation must never be given back");
        _gateway.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PaymentStatuses.Refused)]
    [InlineData(PaymentStatuses.Cancelled)]
    [InlineData(PaymentStatuses.MakePaymentFailed)]
    public async Task A_charge_that_will_never_settle_gives_the_reservation_back(string status)
    {
        GivenPayment(ChargeKey, status);

        await Processor().RecoverStaleAsync(TenantId, default);

        _subscriptions.Verify(
            repository => repository.TryReleaseSettlementAsync(
                TenantId, "sub-1", ReservationId, It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyNeverPromoted();
    }

    [Theory]
    [InlineData(PaymentStatuses.Initiating)]
    [InlineData(PaymentStatuses.Processing)]
    public async Task An_authorization_still_in_flight_is_left_alone(string status)
    {
        GivenPayment(ChargeKey, status);

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(0, "guessing either way here is how a subscriber loses paid units");
        VerifyNeverReleased();
        VerifyNeverPromoted();
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task No_payment_record_is_resolved_by_replaying_the_charge_not_by_assuming()
    {
        // A request that timed out may have been collected and never answered, so the absence of a
        // record proves nothing. The replay carries the reservation's own key, so a provider that
        // already collected answers with that charge rather than raising a second one.
        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(1);
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.Is<SubscriptionChargeRequest>(request =>
                    request.AmountMinor == 5_437 &&
                    request.OrderId == SubscriptionConstants.SettlementOrderIdFor(
                        "sub-1",
                        SettlementReservationKind.QuantityIncrease,
                        ReservationId)),
                ChargeKey,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _subscriptions.Verify(
            repository => repository.TryPromoteQuantityReservationAsync(
                TenantId, "sub-1", ReservationId,
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), "pay-2",
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_replay_the_provider_refuses_gives_the_reservation_back()
    {
        GivenReplayFailure(PaymentFailureKind.ProviderRejected, "card_declined");

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(1);
        _subscriptions.Verify(
            repository => repository.TryReleaseSettlementAsync(
                TenantId, "sub-1", ReservationId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(PaymentFailureKind.Timeout)]
    [InlineData(PaymentFailureKind.Unavailable)]
    [InlineData(PaymentFailureKind.ProviderFailure)]
    [InlineData(PaymentFailureKind.Unexpected)]
    public async Task A_replay_that_goes_unanswered_keeps_the_reservation(PaymentFailureKind kind)
    {
        GivenReplayFailure(kind, "provider_unreachable");

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(0);
        VerifyNeverReleased();
        VerifyNeverPromoted();
    }

    [Fact]
    public async Task The_replay_uses_the_routing_the_reservation_recorded()
    {
        // Not the billing account as it stands now. A card swapped since would send the replay to a
        // different card, which is not a replay of anything, and the idempotency key would then be
        // guarding a charge nobody raised.
        SubscriptionChargeRequest? sent = null;
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((SubscriptionChargeRequest request, string _, string _, CancellationToken _) =>
                sent = request)
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-2", "corr-1"));

        await Processor().RecoverStaleAsync(TenantId, default);

        sent!.StoredPaymentMethodId.Should().Be("pm-1");
        sent.ProviderCustomerId.Should().Be("cus_123");
        sent.ProviderName.Should().Be("STRIPE");
        sent.OrganizationId.Should().Be("org-merchant");
        sent.AmountMinor.Should().Be(5_437);
    }

    [Fact]
    public async Task A_card_removed_since_the_reservation_is_not_evidence_that_nothing_was_charged()
    {
        // The old behaviour read the billing account and released when it found no card. A card
        // deleted after the money moved looks exactly like a charge that never happened, so it
        // abandoned paid increases. The provider decides now, not the account: replayed under the
        // recorded routing, a deleted card is refused, and the refusal is what releases.
        GivenReplayFailure(PaymentFailureKind.Timeout, "provider_unreachable");

        await Processor().RecoverStaleAsync(TenantId, default);

        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyNeverReleased();
    }

    [Fact]
    public async Task A_reservation_with_no_recorded_routing_is_held_rather_than_guessed_at()
    {
        // Nothing to replay with, so nothing can establish what happened. Releasing would be a
        // guess, and the guess costs the subscriber either their money or their units.
        _subscription.SettlementReservation!.StoredPaymentMethodId = string.Empty;

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(0);
        VerifyNeverReleased();
        VerifyNeverPromoted();
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_charge_is_looked_up_under_both_names_before_anything_is_replayed()
    {
        await Processor().RecoverStaleAsync(TenantId, default);

        _payments.Verify(
            repository => repository.GetByIdempotencyKeyAsync(
                TenantId, ChargeKey, It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            repository => repository.GetByIdempotencyKeyAsync(
                TenantId,
                SubscriptionConstants.RecordedSettlementKeyFor(ChargeKey),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_settled_plan_change_is_applied_from_the_terms_the_reservation_carried()
    {
        // The same recovery, for the other kind of settlement. The terms come from the reservation,
        // never from the catalogue as it stands now: a price edited in between must not change what
        // the customer has already paid for.
        _subscription.SettlementReservation = new SettlementReservation
        {
            ReservationId = ReservationId,
            Kind = SettlementReservationKind.PlanChange,
            ChargeAmountMinor = 12_000,
            BillingAccountId = "acct-1",
            ProviderName = "STRIPE",
            ProviderCustomerId = "cus_123",
            StoredPaymentMethodId = "pm-1",
            ReservedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc),
            CorrelationId = "corr-1",
            PlanChange = new ReservedPlanChange
            {
                Plan = new PlanSnapshot { Code = "scale", DisplayName = "Scale" },
                Price = new PriceSnapshot { UnitAmountMinor = 20_000, CurrencyCode = "CHF" },
                QuantityItems = [Item(5)],
                Schedule = new SubscriptionPlanSchedule(
                    new BillingSchedule(),
                    new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc),
                    new BillingSchedule(),
                    new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc)),
                OutgoingUsagePeriod = new PendingUsagePeriod(),
                NewCreditBalanceMinor = 400
            }
        };

        SubscriptionOutboxEvent? announced = null;

        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                TenantId, "sub-1", It.IsAny<int>(), ReservationId,
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()))
            .Callback((string _, string _, int _, string? _, PlanSnapshot _, PriceSnapshot _,
                    List<SubscriptionQuantityItem> _, SubscriptionPlanSchedule _,
                    PendingUsagePeriod _, long _, string? _, SubscriptionOutboxEvent raised,
                    CancellationToken _, SubscriptionDocumentSource? _,
                    PendingAnnualPeriod? _) => announced = raised)
            .ReturnsAsync(true);

        GivenPayment(ChargeKey, PaymentStatuses.Captured);

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(1);
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                TenantId,
                "sub-1",
                It.IsAny<int>(),
                ReservationId,
                It.Is<PlanSnapshot>(plan => plan.Code == "scale"),
                It.IsAny<PriceSnapshot>(),
                It.Is<List<SubscriptionQuantityItem>>(items => items.Single().Quantity == 5),
                It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(),
                400,
                "pay-1",
                It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>()),
            Times.Once);

        // The event has to name the plan arrived at, not the plan left. Built from the subscription
        // as loaded, both fields carried the old code and a consumer would never learn that a paid
        // change had happened — the one thing this recovery exists to guarantee.
        announced.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<SubscriptionLifecycleEvent>(
            announced!.Payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        payload!.PlanCode.Should().Be("scale");
        payload.PreviousPlanCode.Should().Be("team");
    }

    /// <summary>
    /// Recovering an opening-stub upgrade installs the replacement year the reservation carried,
    /// naming the payment this recovery confirmed.
    /// </summary>
    /// <remarks>
    /// The regression this guards: the request path used to stamp the confirmed payment onto the
    /// replacement year <em>after</em> the reservation had already been persisted, so it changed
    /// only the in-memory copy. A recovery replaying that reservation read the un-stamped value and
    /// installed the original year's payment id, leaving the same settled operation with different
    /// state depending on whether a process happened to die. Both paths now stamp it at promotion,
    /// through <see cref="PendingAnnualPeriod.SettledBy"/>.
    /// </remarks>
    [Fact]
    public async Task A_recovered_stub_upgrade_installs_the_year_naming_the_payment_it_confirmed()
    {
        _subscription.SettlementReservation = new SettlementReservation
        {
            ReservationId = ReservationId,
            Kind = SettlementReservationKind.PlanChange,
            ChargeAmountMinor = 12_000,
            BillingAccountId = "acct-1",
            ProviderName = "STRIPE",
            ProviderCustomerId = "cus_123",
            StoredPaymentMethodId = "pm-1",
            ReservedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc),
            CorrelationId = "corr-1",
            PlanChange = new ReservedPlanChange
            {
                Plan = new PlanSnapshot { Code = "scale", DisplayName = "Scale" },
                Price = new PriceSnapshot { UnitAmountMinor = 20_000, CurrencyCode = "CHF" },
                QuantityItems = [Item(5)],
                Schedule = new SubscriptionPlanSchedule(
                    new BillingSchedule(),
                    new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    new BillingSchedule(),
                    new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
                OutgoingUsagePeriod = new PendingUsagePeriod(),
                NewCreditBalanceMinor = 400,
                // As reserved: still naming the payment that bought the terms being replaced,
                // because the adjustment's own payment did not exist when this was written.
                ReplacementPendingAnnualPeriod = new PendingAnnualPeriod
                {
                    StartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndUtc = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    GrossAmountMinor = 1_200_000,
                    AmountMinor = 1_200_000,
                    NetAmountMinor = 1_200_000,
                    IsPrepaid = true,
                    PaymentDetailId = "pay-original"
                }
            }
        };

        PendingAnnualPeriod? installed = null;

        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                TenantId, "sub-1", It.IsAny<int>(), ReservationId,
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<SubscriptionPlanSchedule>(),
                It.IsAny<PendingUsagePeriod>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>(),
                It.IsAny<SubscriptionDocumentSource?>(), It.IsAny<PendingAnnualPeriod?>()))
            .Callback((string _, string _, int _, string? _, PlanSnapshot _, PriceSnapshot _,
                    List<SubscriptionQuantityItem> _, SubscriptionPlanSchedule _,
                    PendingUsagePeriod _, long _, string? _, SubscriptionOutboxEvent _,
                    CancellationToken _, SubscriptionDocumentSource? _,
                    PendingAnnualPeriod? annual) => installed = annual)
            .ReturnsAsync(true);

        GivenPayment(ChargeKey, PaymentStatuses.Captured);

        var resolved = await Processor().RecoverStaleAsync(TenantId, default);

        resolved.Should().Be(1);
        installed.Should().NotBeNull();

        // The payment this recovery confirmed, which is what the request path would have installed
        // for the same reservation and the same charge.
        installed!.PaymentDetailId.Should().Be("pay-1");

        // Every other frozen figure is the reservation's, untouched.
        installed.AmountMinor.Should().Be(1_200_000);
        installed.StartUtc.Should().Be(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        installed.EndUtc.Should().Be(new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        installed.IsPrepaid.Should().BeTrue();

        // Stamping produces a copy, so the reservation still reads as it was persisted and a
        // second replay starts from the same place this one did.
        _subscription.SettlementReservation.PlanChange!.ReplacementPendingAnnualPeriod!
            .PaymentDetailId.Should().Be("pay-original");
    }

    private void GivenPayment(string idempotencyKey, string status) =>
        _payments
            .Setup(repository => repository.GetByIdempotencyKeyAsync(
                TenantId, idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-1",
                TenantId = TenantId,
                PaymentStatus = status
            });

    private void GivenReplayFailure(PaymentFailureKind kind, string errorCode) =>
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                kind, errorCode, "No.", "corr-1"));

    private void VerifyNeverPromoted() =>
        _subscriptions.Verify(
            repository => repository.TryPromoteQuantityReservationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(), It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private void VerifyNeverReleased() =>
        _subscriptions.Verify(
            repository => repository.TryReleaseSettlementAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

    private SubscriptionSettlementReservationProcessor Processor() => new(
        _subscriptions.Object,
        _payments.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<SubscriptionSettlementReservationProcessor>.Instance,
        _time);

    private static SubscriptionQuantityItem Item(long quantity) => new()
    {
        ItemKey = "user",
        UnitLabel = "user",
        Quantity = quantity,
        UnitAmountMinor = 14_500
    };
}
