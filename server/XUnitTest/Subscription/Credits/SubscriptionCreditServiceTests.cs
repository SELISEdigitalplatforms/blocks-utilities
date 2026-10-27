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
using Subscription.DomainService.Requests;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription.Credits;

/// <summary>
/// Quoting, buying, granting and moving meter credits (spec 002, phase B).
/// </summary>
/// <remarks>
/// Guards against the ways a credit purchase costs a customer money they should not have spent: a
/// retried request charging twice, a quote that differs from the charge, a one-use code applied
/// twice, a declined card that still grants credits, or credits bought into a subscription that is
/// about to end.
/// </remarks>
public sealed class SubscriptionCreditServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<ICampaignRedemptionRepository> _redemptions = new();
    private readonly Mock<ISubscriptionFinancialDocumentAnnouncer> _documents = new();
    private readonly Mock<ISubscriptionOutboxEventFactory> _events = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly Mock<ISubscriptionAuditTrail> _audit = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly InMemoryCreditGrants _grants = new();
    private readonly InMemoryCreditPurchases _purchases = new();
    private readonly List<string> _publishedEvents = [];

    private SubscriptionDetail _subscription = Subscription();

    public SubscriptionCreditServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1", "Ada", "ada@example.com")));

        _subscriptions
            .Setup(repository => repository.GetAsync(TenantId, OrganizationId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _subscriptions
            .Setup(repository => repository.TryAppendEventAsync(
                TenantId, SubscriptionId, It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _billingAccounts
            .Setup(repository => repository.GetAsync(TenantId, "account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                ItemId = "account-1",
                ProviderName = "Adyen",
                DefaultPaymentMethodId = "card-1"
            });

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        _events
            .Setup(factory => factory.CreateCreditEvent(
                It.IsAny<SubscriptionDetail>(), It.IsAny<string>(), It.IsAny<SubscriptionCreditGrant>(),
                It.IsAny<string>(), It.IsAny<int?>()))
            .Returns((SubscriptionDetail _, string type, SubscriptionCreditGrant _, string _, int? _) =>
            {
                _publishedEvents.Add(type);
                return new SubscriptionOutboxEvent { EventType = type };
            });

        _redemptions
            .Setup(repository => repository.TryReserveAsync(It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CampaignReservationOutcome.Reserved);
    }

    // ---- quote ----------------------------------------------------------------------------

    [Fact]
    public void One_hundred_credits_at_ten_cents_cost_ten_dollars()
    {
        var quote = CreditQuoteCalculator.Quote(
            new MeterTopUpPrice { CurrencyCode = "USD", UnitAmountMinor = 10 },
            new MeterTopUpTerms(),
            100,
            null,
            Now.UtcDateTime);

        quote.GrossMinor.Should().Be(1_000);
        quote.TotalMinor.Should().Be(1_000, "10 credits per dollar means 100 credits are $10 (AC-1)");
    }

    [Fact]
    public void A_twenty_percent_code_takes_two_dollars_off_ten()
    {
        var quote = CreditQuoteCalculator.Quote(
            new MeterTopUpPrice { CurrencyCode = "USD", UnitAmountMinor = 10 },
            new MeterTopUpTerms(),
            100,
            new DiscountTerms { Kind = DiscountKind.Percent, PercentBasisPoints = 2_000 },
            Now.UtcDateTime);

        quote.DiscountMinor.Should().Be(200);
        quote.NetMinor.Should().Be(800, "AC-2");
    }

    [Fact]
    public void Exclusive_tax_is_added_to_the_discounted_price_and_inclusive_tax_is_found_inside_it()
    {
        var price = new MeterTopUpPrice { CurrencyCode = "CHF", UnitAmountMinor = 10 };

        var exclusive = CreditQuoteCalculator.Quote(
            price, new MeterTopUpTerms { TaxRateBasisPoints = 810, TaxMode = TaxMode.Exclusive }, 100, null, Now.UtcDateTime);
        var inclusive = CreditQuoteCalculator.Quote(
            price, new MeterTopUpTerms { TaxRateBasisPoints = 810, TaxMode = TaxMode.Inclusive }, 100, null, Now.UtcDateTime);

        exclusive.TotalMinor.Should().Be(1_081);
        inclusive.TotalMinor.Should().Be(1_000, "an inclusive price already contains its tax");
        (inclusive.NetMinor + inclusive.TaxMinor).Should().Be(inclusive.TotalMinor);
    }

    // ---- preview refusals -----------------------------------------------------------------

    [Fact]
    public async Task Credits_cannot_be_bought_during_a_trial()
    {
        _subscription.Status = SubscriptionStatus.Trialing;

        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("subscription_credits_trialing", "AC-7");
    }

    [Fact]
    public async Task Credits_cannot_be_bought_while_a_cancellation_is_scheduled()
    {
        _subscription.CancelAtPeriodEnd = true;

        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_cancellation_scheduled",
            "credits bought into a subscription about to end would be forfeited at once (AC-8)");
    }

    [Fact]
    public async Task Credits_cannot_be_bought_without_a_saved_card()
    {
        _billingAccounts
            .Setup(repository => repository.GetAsync(TenantId, "account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount { ItemId = "account-1", ProviderName = "Adyen" });

        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_no_payment_method", "AC-9");
    }

    [Fact]
    public async Task A_purchase_outside_the_meters_bounds_is_refused()
    {
        var result = await Service().PreviewAsync(SubscriptionId, Buy(quantity: 5), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_quantity_out_of_range");
    }

    // Stripe does not decline a charge under its minimum: it marks the invoice paid at zero and
    // moves the amount onto the customer's balance, so the purchase stuck and the next invoice
    // collected money for credits never granted. Observed live at 50 minor units.
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task A_preview_at_or_under_the_providers_minimum_charge_is_refused(long unitAmountMinor)
    {
        Priced(unitAmountMinor);

        var result = await Service().PreviewAsync(SubscriptionId, Buy(quantity: 10), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_below_minimum_charge");
    }

    [Fact]
    public async Task A_purchase_at_the_providers_minimum_charge_is_refused_without_charging()
    {
        Priced(5);

        var result = await Service().PurchaseAsync(SubscriptionId, Buy(quantity: 10, key: "k1"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_below_minimum_charge");
        _gateway.Verify(gateway => gateway.ChargeAsync(
            It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_purchase_just_over_the_providers_minimum_charge_is_quoted()
    {
        Priced(5);

        var result = await Service().PreviewAsync(SubscriptionId, Buy(quantity: 11), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.TotalMinor.Should().Be(55);
    }

    [Fact]
    public async Task A_code_that_takes_a_purchase_under_the_minimum_charge_is_refused()
    {
        Priced(6);
        CodeExists(OneUseCreditCode());

        // 60 less 20% is 48: over the floor before the code, under it after.
        var result = await Service().PreviewAsync(SubscriptionId, Buy(quantity: 10, code: "topup20"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_below_minimum_charge");
    }

    [Fact]
    public async Task Credits_are_not_offered_in_a_currency_the_meter_has_no_price_in()
    {
        _subscription.CurrencyCode = "EUR";

        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_currency_not_priced");
    }

    [Fact]
    public async Task A_seat_cannot_be_named_on_a_subscription_without_seats()
    {
        var result = await Service().PreviewAsync(SubscriptionId, Buy(seat: 1), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_seat_not_applicable");
    }

    [Fact]
    public async Task A_seat_beyond_what_was_bought_is_refused()
    {
        Seated(seats: 3);

        var result = await Service().PreviewAsync(SubscriptionId, Buy(seat: 4), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_seat_not_found");
    }

    [Fact]
    public async Task A_subscription_code_is_refused_on_a_credit_purchase()
    {
        CodeExists(new Discount
        {
            ItemId = "disc-1",
            Code = "launch",
            Scope = DiscountScope.Subscription,
            Terms = new DiscountTerms { Code = "launch", Kind = DiscountKind.Percent, PercentBasisPoints = 2_000 }
        });

        var result = await Service().PreviewAsync(SubscriptionId, Buy(code: "launch"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_not_applicable", "AC-3");
    }

    [Fact]
    public async Task A_preview_quotes_the_window_end_as_the_expiry_of_credits_that_expire_with_it()
    {
        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.TotalMinor.Should().Be(1_000);
        result.Value.ExpiresAtUtc.Should().Be(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            "a buyer late in a window has to be told the credits end with it");
    }

    [Fact]
    public async Task A_fixed_lifetime_ends_credits_that_many_days_after_the_purchase()
    {
        _subscription.Plan.Meters[0].TopUp!.ExpiryPolicy = MeterResetPolicy.Never;
        _subscription.Plan.Meters[0].TopUp!.ExpiresAfter = new CreditExpiryDuration { Count = 30, Unit = CreditExpiryUnit.Day };

        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30),
            "credits sold for a set time must end that long after they were bought, whatever the billing calendar does");
    }

    [Fact]
    public async Task A_fixed_lifetime_longer_than_the_window_still_ends_with_the_window()
    {
        _subscription.Plan.Meters[0].TopUp!.ExpiresAfter = new CreditExpiryDuration { Count = 1, Unit = CreditExpiryUnit.Year };

        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.Value!.ExpiresAtUtc.Should().Be(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            "window-bound credits are lost with the window; a lifetime only ever shortens that, never extends it");
    }

    [Fact]
    public async Task A_fixed_lifetime_shorter_than_the_window_ends_the_credits_first()
    {
        _subscription.Plan.Meters[0].TopUp!.ExpiresAfter = new CreditExpiryDuration { Count = 6, Unit = CreditExpiryUnit.Hour };

        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.Value!.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddHours(6),
            "whichever end comes first is the one the subscriber has to be told about");
    }

    // ---- purchase -------------------------------------------------------------------------

    [Fact]
    public async Task A_purchase_charges_once_and_grants_the_credits()
    {
        var result = await Service().PurchaseAsync(SubscriptionId, Buy(key: "k1"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Status.Should().Be("Finalized");
        result.Value.Grant!.Remaining.Should().Be(100, "AC-4");
        result.Value.Grant.Source.Should().Be("Purchased");

        _gateway.Verify(gateway => gateway.ChargeAsync(
            It.Is<SubscriptionChargeRequest>(request =>
                request.AmountMinor == 1_000 &&
                request.GrossAmountMinor == 1_000 &&
                request.StoredPaymentMethodId == "card-1" &&
                request.OrderId.StartsWith("sub:sub-1:cr:", StringComparison.Ordinal) &&
                request.OrderId.Length <= 80),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        _documents.Verify(documents => documents.AnnounceCreditPurchaseAsync(
            It.IsAny<SubscriptionDetail>(), "pay-1", It.Is<FinancialDocumentLine>(line => line.Quantity == 100),
            null, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<FinancialDocumentPerson?>()),
            Times.Once, "every settled purchase owes an invoice");

        _publishedEvents.Should().Equal(SubscriptionConstants.SubscriptionCreditsPurchased);
    }

    [Fact]
    public async Task Retrying_a_purchase_returns_the_first_result_without_charging_again()
    {
        var service = Service();
        var first = await service.PurchaseAsync(SubscriptionId, Buy(key: "k1"), "corr-1", CancellationToken.None);
        var retry = await service.PurchaseAsync(SubscriptionId, Buy(key: "k1"), "corr-2", CancellationToken.None);

        retry.IsSuccess.Should().BeTrue();
        retry.Value!.Replayed.Should().BeTrue();
        retry.Value.Grant!.GrantId.Should().Be(first.Value!.Grant!.GrantId);
        _gateway.Verify(gateway => gateway.ChargeAsync(
            It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once, "a retried request must never charge the card a second time (AC-5)");
        _grants.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_purchase_is_refused()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "k1"), "corr-1", CancellationToken.None);

        var reused = await service.PurchaseAsync(SubscriptionId, Buy(quantity: 200, key: "k1"), "corr-2", CancellationToken.None);

        reused.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        reused.ErrorCode.Should().Be("subscription_credits_idempotency_conflict", "AC-6");
    }

    [Fact]
    public async Task A_purchase_without_an_idempotency_key_is_refused()
    {
        var result = await Service().PurchaseAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_idempotency_key_required");
        _purchases.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_declined_card_grants_nothing_and_gives_a_one_use_code_back()
    {
        CodeExists(OneUseCreditCode());
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "Declined.", "corr-1"));

        var result = await Service().PurchaseAsync(SubscriptionId, Buy(code: "topup20", key: "k1"), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        _grants.All.Should().BeEmpty("a declined charge paid for nothing");
        _purchases.All.Single().Status.Should().Be(CreditPurchaseStatus.Failed);
        _redemptions.Verify(repository => repository.TryReleaseAsync(
            TenantId, "disc-1", _purchases.All.Single().ItemId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once, "a code reserved for a purchase that failed must be usable again");
    }

    [Fact]
    public async Task A_charge_with_no_answer_stays_pending_and_keeps_its_code()
    {
        CodeExists(OneUseCreditCode());
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.Timeout, "provider_timeout", "No answer.", "corr-1"));

        var result = await Service().PurchaseAsync(SubscriptionId, Buy(code: "topup20", key: "k1"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_charge_unresolved");
        var purchase = _purchases.All.Single();
        purchase.Status.Should().Be(CreditPurchaseStatus.Pending,
            "a timeout can follow an authorization; failing it would hide a charge that may still capture");
        purchase.ProviderName.Should().Be("Adyen", "the sweep replays to the provider the first attempt used");
        purchase.StoredPaymentMethodId.Should().Be("card-1");
        _redemptions.Verify(repository => repository.TryReleaseAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never, "releasing the code would let a second purchase use it while the first may have been paid");
    }

    [Fact]
    public async Task A_one_use_code_is_reserved_per_purchase_and_redeemed_once_charged()
    {
        CodeExists(OneUseCreditCode());
        CampaignRedemption? reserved = null;
        _redemptions
            .Setup(repository => repository.TryReserveAsync(It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()))
            .Callback<CampaignRedemption, CancellationToken>((redemption, _) => reserved = redemption)
            .ReturnsAsync(CampaignReservationOutcome.Reserved);

        var result = await Service().PurchaseAsync(SubscriptionId, Buy(code: "topup20", key: "k1"), "corr-1", CancellationToken.None);

        result.Value!.Quote.DiscountMinor.Should().Be(200);
        reserved!.CreditPurchaseId.Should().Be(result.Value.PurchaseId,
            "keyed by the subscription, a second purchase would read as a retry and get the code again");
        reserved.SubscriptionId.Should().Be(result.Value.PurchaseId);
        _redemptions.Verify(repository => repository.TryMarkRedeemedAsync(
            TenantId, "disc-1", result.Value.PurchaseId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_one_use_code_already_held_refuses_the_purchase_before_charging()
    {
        CodeExists(OneUseCreditCode());
        _redemptions
            .Setup(repository => repository.TryReserveAsync(It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CampaignReservationOutcome.HeldByAnotherSubscription);

        var result = await Service().PurchaseAsync(SubscriptionId, Buy(code: "topup20", key: "k1"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_discount_already_redeemed");
        _gateway.Verify(gateway => gateway.ChargeAsync(
            It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_seat_purchase_grants_to_that_seat_only()
    {
        Seated(seats: 3);

        var result = await Service().PurchaseAsync(SubscriptionId, Buy(seat: 2, key: "k1"), "corr-1", CancellationToken.None);

        result.Value!.Grant!.SeatNumber.Should().Be(2, "AC-19");
    }

    // ---- grants and moves -----------------------------------------------------------------

    [Fact]
    public async Task A_free_grant_records_its_reason_and_charges_nothing()
    {
        var result = await Service().GrantAsync(
            SubscriptionId,
            new GrantCreditsRequest { MeterKey = "ai-credit", Quantity = 20, Reason = "Outage on 12 October" },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Source.Should().Be("AdminGranted");
        result.Value.Reason.Should().Be("Outage on 12 October");
        _gateway.Verify(gateway => gateway.ChargeAsync(
            It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _publishedEvents.Should().Equal(SubscriptionConstants.SubscriptionCreditsGranted);
    }

    [Fact]
    public async Task A_free_grant_is_refused_during_a_trial_like_a_purchase()
    {
        _subscription.Status = SubscriptionStatus.Trialing;

        var result = await Service().GrantAsync(
            SubscriptionId,
            new GrantCreditsRequest { MeterKey = "ai-credit", Quantity = 20, Reason = "Goodwill" },
            "corr-1",
            CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_trialing");
    }

    [Fact]
    public async Task Moving_part_of_a_seats_credits_to_the_pool_keeps_their_expiry()
    {
        Seated(seats: 3);
        var expiry = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        _grants.All.Add(LiveGrant("g1", seat: 1, remaining: 30, expiresAt: expiry));

        var result = await Service().MoveAsync(
            SubscriptionId,
            new MoveCreditsRequest { MeterKey = "ai-credit", Quantity = 10, FromSeatNumber = 1 },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.From.Single().Remaining.Should().Be(20);
        var moved = result.Value.To.Single();
        moved.SeatNumber.Should().BeNull("a move to no seat is a move to the pool (AC-23)");
        moved.Remaining.Should().Be(10);
        moved.ExpiresAtUtc.Should().Be(expiry, "moving credits must never extend or shorten them");
        moved.SourceGrantId.Should().Be("g1");
    }

    [Fact]
    public async Task A_move_whose_new_grant_cannot_be_stored_gives_the_credits_back()
    {
        Seated(seats: 3);
        _grants.All.Add(LiveGrant("g1", seat: 1, remaining: 30));
        _grants.InsertFailure = new TimeoutException("store unavailable");

        var act = () => Service().MoveAsync(
            SubscriptionId,
            new MoveCreditsRequest { MeterKey = "ai-credit", Quantity = 10, FromSeatNumber = 1, ToSeatNumber = 2 },
            "corr-1",
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        _grants.All.Should().ContainSingle("nothing arrived at the other seat");
        _grants.All.Single().Remaining.Should().Be(30, "credits taken for a move that failed must not simply vanish");
    }

    [Fact]
    public async Task A_caller_that_goes_away_mid_move_does_not_delete_the_credits()
    {
        Seated(seats: 3);
        _grants.All.Add(LiveGrant("g1", seat: 1, remaining: 30));
        using var gone = new CancellationTokenSource();
        // The caller goes away the moment the first credit has been taken.
        _grants.AfterConsume = gone.Cancel;

        await Service().MoveAsync(
            SubscriptionId,
            new MoveCreditsRequest { MeterKey = "ai-credit", Quantity = 10, FromSeatNumber = 1, ToSeatNumber = 2 },
            "corr-1",
            gone.Token);

        _grants.All.Sum(grant => grant.Remaining).Should().Be(30,
            "once the first credit is taken, the move finishes; a disconnect must not leave it half done");
        _grants.All.Should().Contain(grant => grant.SeatNumber == 2 && grant.Remaining == 10);
    }

    [Fact]
    public async Task Moving_more_than_a_seat_holds_changes_nothing()
    {
        Seated(seats: 3);
        _grants.All.Add(LiveGrant("g1", seat: 1, remaining: 5));

        var result = await Service().MoveAsync(
            SubscriptionId,
            new MoveCreditsRequest { MeterKey = "ai-credit", Quantity = 10, FromSeatNumber = 1, ToSeatNumber = 2 },
            "corr-1",
            CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_insufficient");
        _grants.All.Single().Remaining.Should().Be(5);
    }

    [Fact]
    public async Task Credits_cannot_be_moved_on_a_subscription_without_seats()
    {
        var result = await Service().MoveAsync(
            SubscriptionId,
            new MoveCreditsRequest { MeterKey = "ai-credit", Quantity = 1, FromSeatNumber = null, ToSeatNumber = 1 },
            "corr-1",
            CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_not_member_based");
    }

    // ---- recovery -------------------------------------------------------------------------

    [Fact]
    public async Task A_purchase_charged_before_a_crash_is_granted_by_the_recovery_sweep()
    {
        var purchase = StalePurchase(CreditPurchaseStatus.Charged, paymentId: "pay-1");

        var settled = await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        purchase.Status.Should().Be(CreditPurchaseStatus.Finalized, "the card was charged, so the credits are owed (AC-26)");
        _grants.All.Should().ContainSingle(grant => grant.PurchaseId == purchase.ItemId && grant.Remaining == 100);
        _documents.Verify(documents => documents.AnnounceCreditPurchaseAsync(
            It.IsAny<SubscriptionDetail>(), "pay-1", It.IsAny<FinancialDocumentLine>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<FinancialDocumentPerson?>()), Times.Once);
    }

    [Fact]
    public async Task A_pending_purchase_whose_charge_went_through_is_finished()
    {
        var purchase = StalePurchase(CreditPurchaseStatus.Pending);
        PaymentRecorded(purchase, PaymentStatuses.Captured);

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.Finalized);
        purchase.PaymentId.Should().Be("pay-9");
    }

    [Fact]
    public async Task A_pending_purchase_with_no_charge_fails_and_gives_its_code_back()
    {
        var purchase = StalePurchase(CreditPurchaseStatus.Pending);
        purchase.DiscountId = "disc-1";

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.Failed, "no charge was recorded, so no money moved (AC-27)");
        _grants.All.Should().BeEmpty();
        _redemptions.Verify(repository => repository.TryReleaseAsync(
            TenantId, "disc-1", purchase.ItemId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_charge_still_in_flight_is_left_alone_until_the_deadline()
    {
        var purchase = StalePurchase(CreditPurchaseStatus.Pending);
        PaymentRecorded(purchase, PaymentStatuses.Processing);

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.Pending,
            "failing it now could leave a charge that later succeeds with nothing granted");
    }

    [Fact]
    public async Task A_charge_still_unresolved_after_the_deadline_is_handed_to_an_operator()
    {
        var purchase = StalePurchase(CreditPurchaseStatus.Pending, age: TimeSpan.FromHours(25));
        PaymentRecorded(purchase, PaymentStatuses.Processing);

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.NeedsAttention);
    }

    [Fact]
    public async Task A_purchase_still_inside_its_own_request_is_not_touched()
    {
        var purchase = StalePurchase(CreditPurchaseStatus.Pending, age: TimeSpan.FromSeconds(30));

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.Pending, "its own request may still be about to charge it");
    }

    [Fact]
    public async Task A_stripe_purchase_with_no_record_yet_is_replayed_and_finished_when_the_provider_was_paid()
    {
        var purchase = StripePurchase();
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-stripe", "corr-1"));

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.Finalized,
            "Stripe records an invoice only once paid, so a missing record is not proof the card was never charged");
        purchase.PaymentId.Should().Be("pay-stripe");
        _gateway.Verify(gateway => gateway.ChargeAsync(
            It.Is<SubscriptionChargeRequest>(request =>
                request.ProviderName == "Stripe" && request.StoredPaymentMethodId == "pm-1" && request.ProviderCustomerId == "cus-1"),
            SubscriptionConstants.CreditPurchaseChargeKeyFor(purchase.ItemId),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "only a replay under the original key can return the original charge instead of a second one");
    }

    [Fact]
    public async Task A_stripe_purchase_the_provider_refuses_on_replay_fails_and_gives_its_code_back()
    {
        var purchase = StripePurchase();
        purchase.DiscountId = "disc-1";
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "Declined.", "corr-1"));

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.Failed, "the provider answered that no money moved");
        _redemptions.Verify(repository => repository.TryReleaseAsync(
            TenantId, "disc-1", purchase.ItemId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_stripe_purchase_still_unanswered_on_replay_is_held()
    {
        var purchase = StripePurchase();
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.Timeout, "provider_timeout", "No answer.", "corr-1"));

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.Pending,
            "with no answer either way, failing it could leave a paid purchase with nothing granted");
        _grants.All.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Canceled)]
    [InlineData(SubscriptionStatus.IncompleteExpired)]
    public async Task A_stripe_purchase_with_no_record_on_an_ended_subscription_is_not_charged_but_handed_to_an_operator(
        SubscriptionStatus status)
    {
        var purchase = StripePurchase();
        purchase.DiscountId = "disc-1";
        _subscription.Status = status;

        var settled = await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        _gateway.Verify(gateway => gateway.ChargeAsync(
            It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "a replay would charge the card for credits the ended subscription forfeits at once");
        purchase.Status.Should().Be(CreditPurchaseStatus.NeedsAttention,
            "the first attempt may still have taken money Stripe has not reported, so only an operator can settle it");
        _grants.All.Should().BeEmpty();
        _redemptions.Verify(repository => repository.TryReleaseAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_charged_purchase_on_an_ended_subscription_is_handed_to_an_operator_instead_of_granted()
    {
        var purchase = StalePurchase(CreditPurchaseStatus.Charged, paymentId: "pay-1");
        _subscription.Status = SubscriptionStatus.Canceled;

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.NeedsAttention,
            "credits granted into an ended subscription are forfeited at once, so the money is owed back instead");
        _grants.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pending_purchase_charged_before_the_subscription_ended_keeps_its_payment_for_the_operator()
    {
        var purchase = StalePurchase(CreditPurchaseStatus.Pending);
        PaymentRecorded(purchase, PaymentStatuses.Captured);
        _subscription.Status = SubscriptionStatus.Canceled;

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.NeedsAttention);
        purchase.PaymentId.Should().Be("pay-9", "the operator needs the payment to refund");
        _grants.All.Should().BeEmpty();
    }

    private SubscriptionCreditPurchase StripePurchase()
    {
        var purchase = StalePurchase(CreditPurchaseStatus.Pending);
        purchase.ProviderName = "Stripe";
        purchase.ProviderCustomerId = "cus-1";
        purchase.StoredPaymentMethodId = "pm-1";
        return purchase;
    }

    private SubscriptionCreditPurchase StalePurchase(
        CreditPurchaseStatus status,
        string? paymentId = null,
        TimeSpan? age = null)
    {
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        var at = Now.UtcDateTime - (age ?? TimeSpan.FromMinutes(10));
        var purchase = new SubscriptionCreditPurchase
        {
            ItemId = $"{SubscriptionId}:key-1",
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            SubscriptionId = SubscriptionId,
            MeterKey = "ai-credit",
            Quantity = 100,
            BuyerUserId = "user-1",
            CurrencyCode = "USD",
            UnitAmountMinor = 10,
            GrossMinor = 1_000,
            NetMinor = 1_000,
            TotalMinor = 1_000,
            ExpiryPolicy = MeterResetPolicy.Periodic,
            OrderId = SubscriptionConstants.CreditPurchaseOrderIdFor(SubscriptionId, "0123456789abcdef0123456789abcdef"),
            Status = status,
            PaymentId = paymentId,
            CreatedAtUtc = at,
            LastUpdatedAtUtc = at
        };

        _purchases.All.Add(purchase);
        return purchase;
    }

    private void PaymentRecorded(SubscriptionCreditPurchase purchase, string status) =>
        _payments
            .Setup(repository => repository.GetByIdempotencyKeyAsync(
                TenantId, SubscriptionConstants.CreditPurchaseChargeKeyFor(purchase.ItemId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail { ItemId = "pay-9", TenantId = TenantId, PaymentStatus = status });

    // ---- helpers --------------------------------------------------------------------------

    private SubscriptionCreditService Service() => new(
        _contextResolver.Object,
        _subscriptions.Object,
        _billingAccounts.Object,
        _gateway.Object,
        _discounts.Object,
        _redemptions.Object,
        _grants,
        _purchases,
        _documents.Object,
        new CreditLedger(_grants, _usage.Object, _subscriptions.Object, _events.Object, _scheduler.Object),
        _payments.Object,
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        _audit.Object,
        new CreditPurchaseRequestValidator(),
        new GrantCreditsRequestValidator(),
        new MoveCreditsRequestValidator(),
        NullLogger<SubscriptionCreditService>.Instance,
        new ControlledTimeProvider(Now));

    private static CreditPurchaseRequest Buy(
        decimal quantity = 100,
        int? seat = null,
        string? code = null,
        string? key = null) => new()
    {
        MeterKey = "ai-credit",
        Quantity = quantity,
        SeatNumber = seat,
        DiscountCode = code,
        IdempotencyKey = key
    };

    private void Priced(long unitAmountMinor) =>
        _subscription.Plan.Meters[0].TopUp!.UnitPrices =
            [new MeterTopUpPrice { CurrencyCode = "USD", UnitAmountMinor = unitAmountMinor }];

    private void Seated(long seats)
    {
        _subscription.Plan.SubscriberScope = SubscriberScope.User;
        _subscription.QuantityItems = [new SubscriptionQuantityItem { ItemKey = "seat", Quantity = seats, CountsMembers = true }];
    }

    private void CodeExists(Discount discount) =>
        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, discount.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(discount);

    private static Discount OneUseCreditCode() => new()
    {
        ItemId = "disc-1",
        Code = "topup20",
        Scope = DiscountScope.CreditPurchase,
        Terms = new DiscountTerms { Code = "topup20", Kind = DiscountKind.Percent, PercentBasisPoints = 2_000 },
        Campaign = new CampaignTerms { OneUsePerOrganization = true }
    };

    private static SubscriptionCreditGrant LiveGrant(
        string id,
        int? seat,
        decimal remaining,
        DateTime? expiresAt = null) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        SubscriptionId = SubscriptionId,
        MeterKey = "ai-credit",
        SeatNumber = seat,
        Quantity = remaining,
        Remaining = remaining,
        ExpiryPolicy = expiresAt is null ? MeterResetPolicy.Never : MeterResetPolicy.Periodic,
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
        BillingAccountId = "account-1",
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
            DisplayName = "Pro",
            PlanVersion = 3,
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "ai-credit",
                    DisplayName = "AI",
                    UnitLabel = "credit",
                    IncludedQuantity = 100,
                    TopUp = new MeterTopUpTerms
                    {
                        Enabled = true,
                        UnitPrices = [new MeterTopUpPrice { CurrencyCode = "USD", UnitAmountMinor = 10 }],
                        MinQuantity = 10,
                        MaxQuantity = 1_000,
                        ExpiryPolicy = MeterResetPolicy.Periodic
                    }
                }
            ]
        }
    };
}
