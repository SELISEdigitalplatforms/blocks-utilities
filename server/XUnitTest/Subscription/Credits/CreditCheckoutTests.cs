using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
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
/// Checkout on first top-up (spec 003): a credit purchase with no saved card is paid at a hosted
/// checkout that saves the card, and is granted only on the provider's word.
/// </summary>
/// <remarks>
/// Guards against the ways this costs a customer money or credits: a second session for one
/// purchase, credits granted on a browser redirect, a paid checkout never granted, a held discount
/// code never given back, credits granted into an ended subscription, and a card the subscriber
/// chose being replaced by the one a top-up saved.
/// </remarks>
public sealed class CreditCheckoutTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";
    private const string PaymentId = "pay-c1";
    private const string CheckoutUrl = "https://checkout.example/c1";

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly ControlledTimeProvider _time = new(Now);
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<ICampaignRedemptionRepository> _redemptions = new();
    private readonly Mock<ISubscriptionFinancialDocumentAnnouncer> _documents = new();
    private readonly Mock<ISubscriptionOutboxEventFactory> _events = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentService> _paymentService = new();
    private readonly Mock<IStoredPaymentMethodRepository> _storedMethods = new();
    private readonly Mock<ISubscriptionPaymentReconciler> _reconciler = new();
    private readonly InMemoryCreditGrants _grants = new();
    private readonly InMemoryCreditPurchases _purchases = new();
    private readonly List<string> _publishedEvents = [];
    private readonly List<MakePaymentRequest> _checkoutsOpened = [];

    private SubscriptionDetail _subscription = Subscription();
    private PaymentDetail? _payment;

    public CreditCheckoutTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1", "Ada", "ada@example.com")));

        _subscriptions
            .Setup(repository => repository.GetAsync(TenantId, OrganizationId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);
        _subscriptions
            .Setup(repository => repository.TryAppendEventAsync(
                TenantId, SubscriptionId, It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // The account a $0 plan leaves behind: pinned to a provider, with no card on it.
        _billingAccounts
            .Setup(repository => repository.GetAsync(TenantId, "account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new BillingAccount
            {
                ItemId = "account-1",
                ProviderName = PaymentConstants.StripeProvider,
                ProviderId = "provider-row-1",
                ProviderOrganizationId = "merchant-org",
                BillingEmail = "billing@example.com"
            });
        _billingAccounts
            .Setup(repository => repository.TrySetDefaultPaymentMethodIfNoneAsync(
                TenantId, "account-1", It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _paymentService
            .Setup(service => service.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((MakePaymentRequest request, string _, string _, CancellationToken _) => _checkoutsOpened.Add(request))
            .ReturnsAsync(new PaymentOperationResult
            {
                IsSuccess = true,
                Payment = new PaymentResponse
                {
                    PaymentDetailId = PaymentId,
                    PaymentStatus = PaymentStatuses.Processing,
                    RedirectUrl = CheckoutUrl,
                    ResolvedProviderId = "provider-row-1",
                    ExpiresAtUtc = Now.UtcDateTime.AddMinutes(60)
                }
            });

        _payments
            .Setup(repository => repository.GetByIdAsync(TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _payment);

        _storedMethods
            .Setup(repository => repository.ListActiveAsync(
                TenantId, It.IsAny<IReadOnlyCollection<StoredPaymentMethodLookupScope>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            [
                new StoredPaymentMethod
                {
                    ItemId = "card-new",
                    ProviderName = PaymentConstants.StripeProvider,
                    ProviderPayerReference = "cus_1",
                    ShopperReference = "shopper-1"
                }
            ]);

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
    public async Task With_no_saved_card_the_preview_offers_a_checkout_instead_of_refusing()
    {
        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue("a buyer with no card pays at a checkout (AC-1)");
        result.Value!.PaymentPath.Should().Be("Checkout");
        result.Value.TotalMinor.Should().Be(1_000);
    }

    [Fact]
    public async Task A_purchase_a_code_took_to_nothing_still_needs_a_saved_card()
    {
        Priced(0);

        var result = await Service().PreviewAsync(SubscriptionId, Buy(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_no_payment_method",
            "there is no amount to check out, so nothing would collect the card (AC-19)");
    }

    [Fact]
    public async Task The_minimum_charge_applies_to_a_checkout_too()
    {
        Priced(5);

        var result = await Service().PurchaseAsync(SubscriptionId, Buy(quantity: 10, key: "key-1"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_below_minimum_charge", "AC-18");
        _checkoutsOpened.Should().BeEmpty();
    }

    // ---- opening the checkout -------------------------------------------------------------

    [Fact]
    public async Task A_purchase_with_no_saved_card_opens_a_checkout_that_saves_the_card()
    {
        var result = await Service().PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("AwaitingPayment", "AC-2");
        result.Value.CheckoutUrl.Should().Be(CheckoutUrl);
        result.Value.Grant.Should().BeNull("nothing is granted until the provider confirms the payment");

        var request = _checkoutsOpened.Should().ContainSingle().Subject;
        request.Amount.Should().Be(10m);
        request.SavePaymentMethod.Should().BeTrue();
        request.RecurringModel.Should().Be(PaymentConstants.SubscriptionRecurringModel,
            "later top-ups charge the saved card under the Subscription model");
        request.ProviderName.Should().Be(PaymentConstants.StripeProvider);
        request.ExpectedProviderId.Should().Be("provider-row-1", "the account's frozen provider row, never re-resolved");
        request.OrganizationId.Should().Be("merchant-org");
        request.PaymentMethodOwnerOrganizationId.Should().Be(OrganizationId);
        request.CheckoutExpiresInMinutes.Should().Be(60);

        _paymentService.Verify(service => service.MakePaymentAsync(
            It.IsAny<MakePaymentRequest>(),
            SubscriptionConstants.CreditPurchaseChargeKeyFor($"{SubscriptionId}:key-1"),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        var stored = _purchases.All.Should().ContainSingle().Subject;
        stored.PaymentPath.Should().Be(CreditPaymentPath.Checkout);
        stored.PaymentId.Should().Be(PaymentId);
        stored.CheckoutExpiresAtUtc.Should().Be(Now.UtcDateTime.AddMinutes(60));
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Retrying_the_same_key_returns_the_same_checkout_without_opening_another()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);

        var retry = await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-2", CancellationToken.None);

        retry.Value!.CheckoutUrl.Should().Be(CheckoutUrl, "AC-3");
        retry.Value.Replayed.Should().BeTrue();
        _checkoutsOpened.Should().ContainSingle();
    }

    [Fact]
    public async Task The_same_key_for_a_different_purchase_is_refused()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);

        var reused = await service.PurchaseAsync(SubscriptionId, Buy(quantity: 200, key: "key-1"), "corr-2", CancellationToken.None);

        reused.ErrorCode.Should().Be("subscription_credits_idempotency_conflict", "AC-4");
    }

    [Fact]
    public async Task A_second_checkout_is_refused_while_one_is_open_and_names_the_open_one()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);

        var second = await service.PurchaseAsync(SubscriptionId, Buy(key: "key-2"), "corr-2", CancellationToken.None);

        second.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        second.ErrorCode.Should().Be("subscription_credits_checkout_in_progress", "AC-5");
        second.ValidationErrors!["checkoutUrl"].Should().Equal(CheckoutUrl);
        _checkoutsOpened.Should().ContainSingle();
    }

    [Fact]
    public async Task A_checkout_the_provider_refuses_to_open_fails_the_purchase_and_gives_the_code_back()
    {
        CodeExists(OneUseCreditCode());
        _paymentService
            .Setup(service => service.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOperationResult
            {
                IsSuccess = false,
                FailureKind = PaymentFailureKind.ProviderRejected,
                ErrorCode = "payment_rejected",
                ErrorMessage = "No."
            });

        var result = await Service().PurchaseAsync(SubscriptionId, Buy(code: "TOPUP20", key: "key-1"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _purchases.All.Single().Status.Should().Be(CreditPurchaseStatus.Failed);
        _redemptions.Verify(repository => repository.TryReleaseAsync(
            TenantId, "disc-1", $"{SubscriptionId}:key-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- settlement -----------------------------------------------------------------------

    [Fact]
    public async Task The_buyer_returning_proves_nothing_until_the_provider_confirms()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        // Authorized, but no webhook has said so.
        _payment = Payment(PaymentStatuses.Authorized, webhookConfirmed: false);

        var returned = await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-2", CancellationToken.None);

        returned.Value!.Status.Should().Be("AwaitingPayment", "AC-6");
        _grants.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_confirmed_payment_grants_the_credits_invoices_them_and_adopts_the_card()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _payment = Payment(PaymentStatuses.Captured);

        var settled = await service.SettleCheckoutsForPaymentsAsync(TenantId, ["unrelated", PaymentId], CancellationToken.None);

        settled.Should().Be(1);
        var purchase = _purchases.All.Single();
        purchase.Status.Should().Be(CreditPurchaseStatus.Finalized, "AC-7");
        _grants.All.Should().ContainSingle(grant => grant.Remaining == 100 && grant.Source == CreditGrantSource.Purchased);
        _publishedEvents.Should().Contain(SubscriptionConstants.SubscriptionCreditsPurchased);
        _documents.Verify(documents => documents.AnnounceCreditPurchaseAsync(
            It.IsAny<SubscriptionDetail>(), PaymentId, It.IsAny<FinancialDocumentLine>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<FinancialDocumentPerson?>()), Times.Once);

        _billingAccounts.Verify(repository => repository.TrySetDefaultPaymentMethodIfNoneAsync(
            TenantId, "account-1", "cus_1", "card-new", "merchant-org", It.IsAny<CancellationToken>()), Times.Once,
            "the card the checkout saved becomes the default only if there is none (AC-8)");
        purchase.CardAdoptedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Settling_twice_grants_once()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _payment = Payment(PaymentStatuses.Captured);

        await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);
        var again = await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        again.Should().Be(0);
        _grants.All.Should().ContainSingle();
    }

    [Fact]
    public async Task An_adyen_card_with_no_customer_id_is_still_adopted()
    {
        _storedMethods
            .Setup(repository => repository.ListActiveAsync(
                TenantId, It.IsAny<IReadOnlyCollection<StoredPaymentMethodLookupScope>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new StoredPaymentMethod { ItemId = "adyen-card", ProviderName = PaymentConstants.AdyenOnlineProvider }
            ]);
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _payment = Payment(PaymentStatuses.Authorized);

        await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        _billingAccounts.Verify(repository => repository.TrySetDefaultPaymentMethodIfNoneAsync(
            TenantId, "account-1", null, "adyen-card", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once,
            "an Adyen card carries no customer id and must not be skipped for it (AC-9)");
    }

    [Fact]
    public async Task A_card_the_provider_has_not_reported_yet_does_not_hold_the_grant_back_and_is_adopted_later()
    {
        var methods = new List<StoredPaymentMethod>();
        _storedMethods
            .Setup(repository => repository.ListActiveAsync(
                TenantId, It.IsAny<IReadOnlyCollection<StoredPaymentMethodLookupScope>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => methods);
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _payment = Payment(PaymentStatuses.Captured);

        await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        var purchase = _purchases.All.Single();
        purchase.Status.Should().Be(CreditPurchaseStatus.Finalized);
        purchase.CardAdoptedAtUtc.Should().BeNull();

        methods.Add(new StoredPaymentMethod { ItemId = "card-late", ProviderName = PaymentConstants.StripeProvider, ProviderPayerReference = "cus_1" });
        _time.Advance(TimeSpan.FromMinutes(5));
        await service.RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.CardAdoptedAtUtc.Should().NotBeNull();
        _billingAccounts.Verify(repository => repository.TrySetDefaultPaymentMethodIfNoneAsync(
            TenantId, "account-1", "cus_1", "card-late", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task On_return_a_session_the_provider_reports_paid_is_granted_without_waiting_for_the_webhook()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _reconciler
            .Setup(reconciler => reconciler.TryReconcileAsync(TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .Callback(() => _payment = Payment(PaymentStatuses.Captured))
            .ReturnsAsync(true);

        var returned = await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-2", CancellationToken.None);

        returned.Value!.Status.Should().Be("Finalized", "AC-11");
        returned.Value.Grant!.Remaining.Should().Be(100);
    }

    [Fact]
    public async Task A_declined_checkout_fails_and_gives_the_code_back()
    {
        CodeExists(OneUseCreditCode());
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(code: "TOPUP20", key: "key-1"), "corr-1", CancellationToken.None);
        _payment = Payment(PaymentStatuses.Refused, webhookConfirmed: true);

        await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        var purchase = _purchases.All.Single();
        purchase.Status.Should().Be(CreditPurchaseStatus.Failed, "AC-13");
        purchase.FailureCode.Should().Be("subscription_credits_checkout_declined");
        _grants.All.Should().BeEmpty();
        _redemptions.Verify(repository => repository.TryReleaseAsync(
            TenantId, "disc-1", purchase.ItemId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_payment_confirmed_after_the_subscription_ended_is_not_granted_and_goes_to_an_operator()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _subscription.Status = SubscriptionStatus.Canceled;
        _payment = Payment(PaymentStatuses.Captured);

        await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        var purchase = _purchases.All.Single();
        purchase.Status.Should().Be(CreditPurchaseStatus.NeedsAttention, "AC-14");
        purchase.FailureCode.Should().Be("subscription_credits_subscription_ended");
        _grants.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Credits_that_would_arrive_already_expired_are_not_granted()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        var quotedExpiry = _purchases.All.Single().ExpiresAtUtc;
        quotedExpiry.Should().Be(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), "fixed at quote time");
        _time.Advance(TimeSpan.FromDays(20));
        _payment = Payment(PaymentStatuses.Captured);

        await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        _purchases.All.Single().Status.Should().Be(CreditPurchaseStatus.NeedsAttention, "AC-15");
        _grants.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_payment_confirmed_before_the_quoted_expiry_keeps_the_quoted_expiry()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(30));
        _payment = Payment(PaymentStatuses.Captured);

        await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        _grants.All.Single().ExpiresAtUtc.Should().Be(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_payment_confirmed_after_the_purchase_was_failed_goes_to_an_operator()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _purchases.All.Single().Status = CreditPurchaseStatus.Failed;
        _payment = Payment(PaymentStatuses.Captured);

        await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        var purchase = _purchases.All.Single();
        purchase.Status.Should().Be(CreditPurchaseStatus.NeedsAttention, "AC-16");
        purchase.FailureCode.Should().Be("subscription_credits_paid_after_failure");
        _grants.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_payment_for_a_different_amount_than_quoted_goes_to_an_operator()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _payment = Payment(PaymentStatuses.Captured, amount: 9.99);

        await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        _purchases.All.Single().FailureCode.Should().Be("subscription_credits_amount_mismatch", "AC-17");
        _grants.All.Should().BeEmpty();
    }

    // ---- recovery sweep -------------------------------------------------------------------

    [Fact]
    public async Task An_unpaid_checkout_past_its_expiry_and_grace_fails_and_gives_the_code_back()
    {
        CodeExists(OneUseCreditCode());
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(code: "TOPUP20", key: "key-1"), "corr-1", CancellationToken.None);
        _payment = Payment(PaymentStatuses.Processing, webhookConfirmed: false);

        _time.Advance(TimeSpan.FromMinutes(70));
        await service.RecoverStaleAsync(TenantId, CancellationToken.None);
        _purchases.All.Single().Status.Should().Be(CreditPurchaseStatus.AwaitingPayment, "still inside the grace period");

        _time.Advance(TimeSpan.FromMinutes(10));
        await service.RecoverStaleAsync(TenantId, CancellationToken.None);

        var purchase = _purchases.All.Single();
        purchase.Status.Should().Be(CreditPurchaseStatus.Failed, "AC-12");
        purchase.FailureCode.Should().Be("subscription_credits_checkout_expired");
        _redemptions.Verify(repository => repository.TryReleaseAsync(
            TenantId, "disc-1", purchase.ItemId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_sweep_grants_a_checkout_paid_while_the_webhook_was_lost()
    {
        var service = Service();
        await service.PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);
        _payment = Payment(PaymentStatuses.Captured);
        _time.Advance(TimeSpan.FromMinutes(5));

        await service.RecoverStaleAsync(TenantId, CancellationToken.None);

        _purchases.All.Single().Status.Should().Be(CreditPurchaseStatus.Finalized);
        _grants.All.Should().ContainSingle();
    }

    [Fact]
    public async Task A_checkout_purchase_interrupted_before_its_session_is_failed_when_no_payment_exists()
    {
        var purchase = StaleCheckout();

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.Failed, "AC-20");
        purchase.FailureCode.Should().Be("subscription_credits_checkout_not_started");
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_checkout_purchase_interrupted_after_its_session_opened_adopts_the_session()
    {
        var purchase = StaleCheckout();
        _payments
            .Setup(repository => repository.GetByIdempotencyKeyAsync(
                TenantId, SubscriptionConstants.CreditPurchaseChargeKeyFor(purchase.ItemId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = PaymentId,
                TenantId = TenantId,
                PaymentStatus = PaymentStatuses.Processing,
                RedirectUrl = CheckoutUrl,
                ExpirationDate = Now.UtcDateTime.AddMinutes(50)
            });

        await Service().RecoverStaleAsync(TenantId, CancellationToken.None);

        purchase.Status.Should().Be(CreditPurchaseStatus.AwaitingPayment, "AC-20");
        purchase.PaymentId.Should().Be(PaymentId);
        purchase.CheckoutUrl.Should().Be(CheckoutUrl);
    }

    [Fact]
    public async Task A_purchase_with_a_saved_card_still_charges_it_and_opens_no_checkout()
    {
        _billingAccounts
            .Setup(repository => repository.GetAsync(TenantId, "account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount { ItemId = "account-1", ProviderName = "Adyen", DefaultPaymentMethodId = "card-1" });
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        var result = await Service().PurchaseAsync(SubscriptionId, Buy(key: "key-1"), "corr-1", CancellationToken.None);

        result.Value!.Status.Should().Be("Finalized", "AC-21");
        result.Value.Quote.PaymentPath.Should().Be("StoredCard");
        _checkoutsOpened.Should().BeEmpty();
    }

    // ---- helpers --------------------------------------------------------------------------

    private SubscriptionCreditPurchase StaleCheckout()
    {
        var at = Now.UtcDateTime - TimeSpan.FromMinutes(10);
        var purchase = new SubscriptionCreditPurchase
        {
            ItemId = $"{SubscriptionId}:key-1",
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            SubscriptionId = SubscriptionId,
            MeterKey = "ai-credit",
            Quantity = 100,
            CurrencyCode = "USD",
            TotalMinor = 1_000,
            PaymentPath = CreditPaymentPath.Checkout,
            Status = CreditPurchaseStatus.Pending,
            CreatedAtUtc = at,
            LastUpdatedAtUtc = at
        };

        _purchases.All.Add(purchase);
        return purchase;
    }

    private PaymentDetail Payment(string status, bool webhookConfirmed = true, double amount = 10) => new()
    {
        ItemId = PaymentId,
        TenantId = TenantId,
        OrganizationId = "merchant-org",
        PaymentStatus = status,
        Amount = amount,
        CurrencyCode = "USD",
        ShopperReference = "shopper-1",
        WebhookConfirmedAtUtc = webhookConfirmed ? _time.GetUtcNow().UtcDateTime : null
    };

    private SubscriptionCreditService Service()
    {
        var options = new SubscriptionOptions();

        return new SubscriptionCreditService(
            _contextResolver.Object,
            _subscriptions.Object,
            _billingAccounts.Object,
            _gateway.Object,
            _discounts.Object,
            _redemptions.Object,
            _grants,
            _purchases,
            _documents.Object,
            new CreditLedger(_grants, Mock.Of<ISubscriptionUsageRepository>(), _subscriptions.Object, _events.Object,
                Mock.Of<ISubscriptionWorkScheduler>()),
            _payments.Object,
            new SubscriptionOptionsMonitorStub(options),
            Mock.Of<ISubscriptionAuditTrail>(),
            new CreditPurchaseRequestValidator(),
            new GrantCreditsRequestValidator(),
            new MoveCreditsRequestValidator(),
            NullLogger<SubscriptionCreditService>.Instance,
            _time,
            _paymentService.Object,
            new CurrencyMinorUnitResolver(Mock.Of<IOptionsMonitor<PaymentOptions>>(monitor => monitor.CurrentValue == new PaymentOptions())),
            new BillingAccountCardAdopter(_storedMethods.Object, _billingAccounts.Object, NullLogger<BillingAccountCardAdopter>.Instance),
            _reconciler.Object);
    }

    private static CreditPurchaseRequest Buy(
        decimal quantity = 100,
        string? code = null,
        string? key = null) => new()
    {
        MeterKey = "ai-credit",
        Quantity = quantity,
        DiscountCode = code,
        IdempotencyKey = key
    };

    private void Priced(long unitAmountMinor) =>
        _subscription.Plan.Meters[0].TopUp!.UnitPrices =
            [new MeterTopUpPrice { CurrencyCode = "USD", UnitAmountMinor = unitAmountMinor }];

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
            Code = "wallet",
            DisplayName = "Wallet",
            PlanVersion = 1,
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "ai-credit",
                    DisplayName = "AI",
                    UnitLabel = "credit",
                    IncludedQuantity = 0,
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
