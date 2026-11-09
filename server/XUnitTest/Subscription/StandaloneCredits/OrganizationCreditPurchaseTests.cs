using Blocks.Genesis;
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
using XUnitTest.Subscription.Credits;

namespace XUnitTest.Subscription.StandaloneCredits;

/// <summary>
/// Buying and granting standalone credits an organization holds outside any subscription (spec 005).
/// </summary>
/// <remarks>
/// Guards against the ways a standalone purchase could cost an organization money for nothing: a
/// retry charging twice, a grant that lands on a subscription it has nothing to do with, a quote in
/// a currency the offer never priced, a checkout paid and never granted, an edit to the offer
/// moving credits already bought, and a purchase that goes through with sales paused.
/// </remarks>
public sealed class OrganizationCreditPurchaseTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string PaymentId = "pay-checkout-1";

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly ControlledTimeProvider _time = new(Now);
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<ICampaignRedemptionRepository> _redemptions = new();
    private readonly Mock<ISubscriptionFinancialDocumentAnnouncer> _documents = new();
    private readonly Mock<ISubscriptionAuditTrail> _audit = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentService> _paymentService = new();
    private readonly Mock<IStoredPaymentMethodRepository> _storedMethods = new();
    private readonly Mock<IMessageClient> _messages = new();
    private readonly InMemoryCreditGrants _grants = new();
    private readonly InMemoryCreditPurchases _purchases = new();
    private readonly InMemoryMeterCatalogue _catalogue = new();
    private readonly InMemoryCreditOffers _offers = new();
    private readonly List<string> _published = [];
    private readonly List<MakePaymentRequest> _checkoutsOpened = [];

    private BillingAccount? _account = new()
    {
        ItemId = "account-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        ProviderName = PaymentConstants.StripeProvider,
        ProviderId = "provider-row-1",
        DefaultPaymentMethodId = "card-1"
    };

    private PaymentDetail? _payment;

    public OrganizationCreditPurchaseTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1", "Ada", "ada@example.com")));

        _billingAccounts
            .Setup(repository => repository.FindByOrganizationAsync(
                TenantId, OrganizationId, PaymentConstants.StripeProvider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _account);
        _billingAccounts
            .Setup(repository => repository.GetOrCreateAndReconcileAsync(It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount account, CancellationToken _) =>
            {
                account.ItemId = "account-new";
                _account = account;
                return account;
            });
        _billingAccounts
            .Setup(repository => repository.TrySetDefaultPaymentMethodIfNoneAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));

        _messages
            .Setup(client => client.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<SubscriptionLifecycleEvent>>()))
            .Callback((ConsumerMessage<SubscriptionLifecycleEvent> message) => _published.Add(message.Payload.EventType))
            .Returns(Task.CompletedTask);

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
                    RedirectUrl = "https://checkout.example/session",
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

        _redemptions
            .Setup(repository => repository.TryReserveAsync(It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CampaignReservationOutcome.Reserved);

        _catalogue.All.Add(new MeterCatalogueEntry { TenantId = TenantId, MeterKey = "ai", DisplayName = "AI", UnitLabel = "credit" });
        _offers.All.Add(Offer());
    }

    // ---- quote --------------------------------------------------------------------------------

    [Fact]
    public async Task A_preview_is_quoted_in_the_currency_the_buyer_picked()
    {
        var result = await Service().PreviewForOrganizationAsync(Buy(currency: "CHF"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.CurrencyCode.Should().Be("CHF", "AC-1: the buyer picks which of the offer's prices to pay");
        result.Value.UnitAmountMinor.Should().Be(12);
        result.Value.TotalMinor.Should().Be(1_200);
        result.Value.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30), "AC-7: a fixed lifetime runs from the purchase");
    }

    [Fact]
    public async Task A_code_unrestricted_by_plan_applies_to_a_standalone_purchase()
    {
        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(TenantId, OrganizationId, "topup20", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                ItemId = "disc-1",
                Code = "topup20",
                Scope = DiscountScope.CreditPurchase,
                Terms = new DiscountTerms { Code = "topup20", Kind = DiscountKind.Percent, PercentBasisPoints = 2_000 }
            });

        var result = await Service().PreviewForOrganizationAsync(Buy(code: "topup20"), "corr-1", CancellationToken.None);

        result.Value!.DiscountMinor.Should().Be(200, "AC-2");
        result.Value.NetMinor.Should().Be(800);
    }

    [Fact]
    public async Task A_currency_the_offer_never_priced_is_refused()
    {
        var result = await Service().PreviewForOrganizationAsync(Buy(currency: "EUR"), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_credits_currency_not_priced");
    }

    [Fact]
    public async Task A_disabled_offer_refuses_a_quote_and_a_purchase()
    {
        _offers.All[0].Enabled = false;

        var quote = await Service().PreviewForOrganizationAsync(Buy(), "corr-1", CancellationToken.None);
        var purchase = await Service().PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-2", CancellationToken.None);

        quote.ErrorCode.Should().Be("subscription_credits_not_for_sale", "AC-16");
        purchase.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        _purchases.All.Should().BeEmpty();
    }

    [Fact]
    public async Task A_meter_with_no_offer_is_not_for_sale()
    {
        var result = await Service().PreviewForOrganizationAsync(Buy(meterKey: "storage"), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should().Be("subscription_credit_offer_not_found");
    }

    [Fact]
    public async Task The_organizations_own_offer_prices_its_purchase()
    {
        _offers.All.Add(Offer(organizationId: OrganizationId, usdUnitAmountMinor: 8));

        var result = await Service().PreviewForOrganizationAsync(Buy(), "corr-1", CancellationToken.None);

        result.Value!.UnitAmountMinor.Should().Be(8, "AC-25: an organization override wins over the tenant's offer");
    }

    // ---- packs (spec 005 follow-up) ---------------------------------------------------------

    [Fact]
    public async Task A_pack_is_quoted_at_its_own_price_and_its_own_size_outside_the_offers_bounds()
    {
        _offers.All[0].MaxQuantity = 200;
        _offers.All[0].Packs = [Starter()];

        var result = await Service().PreviewForOrganizationAsync(Buy(quantity: 1, pack: "STARTER"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Quantity.Should().Be(500, "a pack's size is the pack's, not the buyer's, and the offer's maximum does not apply");
        result.Value.UnitAmountMinor.Should().Be(8, "the pack's own price, cheaper per credit than the offer's 10");
        result.Value.TotalMinor.Should().Be(4_000);
        result.Value.PackCode.Should().Be("starter", "matched without regard to case, answered as authored");
        result.Value.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30), "expiry is the offer's");
    }

    [Fact]
    public async Task A_pack_the_offer_lacks_or_does_not_price_in_that_currency_is_refused()
    {
        _offers.All[0].Packs = [Starter()];

        var unknown = await Service().PreviewForOrganizationAsync(Buy(pack: "bulk"), "corr-1", CancellationToken.None);
        var unpriced = await Service().PreviewForOrganizationAsync(Buy(currency: "CHF", pack: "starter"), "corr-2", CancellationToken.None);

        unknown.ErrorCode.Should().Be("subscription_credit_pack_not_found");
        unknown.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        unpriced.ErrorCode.Should().Be("subscription_credits_currency_not_priced",
            "a pack priced in USD only cannot be bought in CHF, whatever the offer itself sells in");
    }

    [Fact]
    public async Task A_bought_pack_is_recorded_on_the_purchase_and_a_retry_without_it_is_a_different_purchase()
    {
        _offers.All[0].Packs = [Starter()];

        var bought = await Service().PurchaseForOrganizationAsync(Buy(key: "k1", pack: "starter"), "corr-1", CancellationToken.None);
        var retried = await Service().PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-2", CancellationToken.None);

        bought.IsSuccess.Should().BeTrue(bought.ErrorMessage);
        _purchases.All.Should().ContainSingle().Which.PackCode.Should().Be("starter");
        _purchases.All[0].Quantity.Should().Be(500);
        _grants.All.Should().ContainSingle().Which.Quantity.Should().Be(500);
        retried.ErrorCode.Should().Be("subscription_credits_idempotency_conflict",
            "the same key for a pack and for a free quantity is two different purchases");
    }

    private static CreditPack Starter() => new()
    {
        Code = "starter",
        DisplayName = "Starter pack",
        Quantity = 500,
        UnitPrices = [new MeterTopUpPrice { CurrencyCode = "USD", UnitAmountMinor = 8 }]
    };

    // ---- stored card purchase ---------------------------------------------------------------

    [Fact]
    public async Task A_purchase_charges_the_saved_card_once_and_grants_credits_the_organization_owns()
    {
        var result = await Service().PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Status.Should().Be("Finalized");
        result.Value.Grant!.Remaining.Should().Be(100, "AC-3");

        var grant = _grants.All.Single();
        grant.IsOrganizationOwned.Should().BeTrue("the credits belong to the organization, not to any subscription");
        grant.OrganizationId.Should().Be(OrganizationId);
        grant.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30));

        _gateway.Verify(gateway => gateway.ChargeAsync(
            It.Is<SubscriptionChargeRequest>(request =>
                request.AmountMinor == 1_000 &&
                request.StoredPaymentMethodId == "card-1" &&
                request.SubscriberOrganizationId == OrganizationId &&
                request.OrderId.StartsWith(SubscriptionConstants.OrganizationCreditOrderIdPrefix, StringComparison.Ordinal) &&
                request.Description.Contains("AI credits", StringComparison.Ordinal)),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        _documents.Verify(documents => documents.AnnounceOrganizationCreditPurchaseAsync(
            It.Is<SubscriptionCreditPurchase>(purchase => purchase.PaymentId == "pay-1" && purchase.MeterDisplayName == "AI"),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once, "AC-3: every settled purchase owes an invoice");
        _documents.Verify(documents => documents.AnnounceCreditPurchaseAsync(
            It.IsAny<SubscriptionDetail>(), It.IsAny<string>(), It.IsAny<FinancialDocumentLine>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<FinancialDocumentPerson?>()), Times.Never);

        _published.Should().Equal(SubscriptionConstants.OrganizationCreditsPurchased);
        _purchases.All.Single().OfferVersion.Should().Be(_offers.All[0].Version, "the quote's offer version is recorded");
        _purchases.All.Single().BillingAccountId.Should().Be("account-1");
    }

    [Fact]
    public async Task Retrying_a_purchase_returns_the_first_result_without_charging_again()
    {
        var service = Service();
        var first = await service.PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-1", CancellationToken.None);
        var retry = await service.PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-2", CancellationToken.None);

        retry.Value!.Replayed.Should().BeTrue("AC-5");
        retry.Value.Grant!.GrantId.Should().Be(first.Value!.Grant!.GrantId);
        _gateway.Verify(gateway => gateway.ChargeAsync(
            It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _grants.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_purchase_is_refused()
    {
        var service = Service();
        await service.PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-1", CancellationToken.None);

        var reused = await service.PurchaseForOrganizationAsync(Buy(quantity: 200, key: "k1"), "corr-2", CancellationToken.None);

        reused.ErrorCode.Should().Be("subscription_credits_idempotency_conflict", "AC-6");
    }

    [Fact]
    public async Task Editing_the_offer_after_a_purchase_leaves_the_grants_expiry_where_it_was()
    {
        var service = Service();
        await service.PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-1", CancellationToken.None);

        _offers.All[0].ExpiresAfter = new CreditExpiryDuration { Count = 60, Unit = CreditExpiryUnit.Day };
        _offers.All[0].Version++;

        _grants.All.Single().ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30),
            "AC-15: the expiry was fixed at the quote, not read from the offer later");
        (await service.PreviewForOrganizationAsync(Buy(), "corr-2", CancellationToken.None))
            .Value!.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(60), "a new purchase takes the new terms");
    }

    [Fact]
    public async Task An_offer_with_no_lifetime_grants_credits_that_never_expire()
    {
        _offers.All[0].ExpiresAfter = null;

        var result = await Service().PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-1", CancellationToken.None);

        result.Value!.Grant!.ExpiresAtUtc.Should().BeNull("AC-7");
        _grants.All.Single().ExpiresAtUtc.Should().BeNull();
    }

    // ---- checkout for an organization with no card, and no subscription ----------------------

    [Fact]
    public async Task An_organization_with_no_billing_account_gets_one_and_a_checkout_that_saves_its_card()
    {
        _account = null;

        var result = await Service().PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Status.Should().Be("AwaitingPayment", "AC-4");
        result.Value.CheckoutUrl.Should().Be("https://checkout.example/session");
        result.Value.Grant.Should().BeNull("nothing is granted until the provider confirms the payment");

        _billingAccounts.Verify(repository => repository.GetOrCreateAndReconcileAsync(
            It.Is<BillingAccount>(account => account.OrganizationId == OrganizationId), It.IsAny<CancellationToken>()),
            Times.Once, "a credits-only organization has no account from a signup to charge against");
        var request = _checkoutsOpened.Should().ContainSingle().Subject;
        request.CustomerOrganizationId.Should().Be(OrganizationId);
        request.SavePaymentMethod.Should().BeTrue();
        _purchases.All.Single().BillingAccountId.Should().Be("account-new");
    }

    [Fact]
    public async Task A_paid_checkout_grants_the_credits_and_adopts_the_card_onto_the_organizations_account()
    {
        _account = new BillingAccount
        {
            ItemId = "account-1",
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            ProviderName = PaymentConstants.StripeProvider,
            ProviderId = "provider-row-1"
        };
        var service = Service();
        await service.PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-1", CancellationToken.None);

        _payment = new PaymentDetail
        {
            ItemId = PaymentId,
            TenantId = TenantId,
            OrganizationId = "merchant-org",
            PaymentStatus = PaymentStatuses.Captured,
            WebhookConfirmedAtUtc = Now.UtcDateTime,
            Amount = 10,
            CurrencyCode = "USD",
            ShopperReference = "shopper-1"
        };

        var settled = await service.SettleCheckoutsForPaymentsAsync(TenantId, [PaymentId], CancellationToken.None);

        settled.Should().Be(1);
        _grants.All.Single().Remaining.Should().Be(100, "AC-4: a confirmed checkout grants what was paid for");
        _grants.All.Single().IsOrganizationOwned.Should().BeTrue();
        _billingAccounts.Verify(repository => repository.TrySetDefaultPaymentMethodIfNoneAsync(
            TenantId, "account-1", "cus_1", "card-new", "merchant-org", It.IsAny<CancellationToken>()),
            Times.Once, "AC-4: the card the checkout saved is offered to the account for later purchases");
        _documents.Verify(documents => documents.AnnounceOrganizationCreditPurchaseAsync(
            It.IsAny<SubscriptionCreditPurchase>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- free grants and listing ----------------------------------------------------------

    [Fact]
    public async Task A_free_grant_needs_a_reason_and_takes_the_offers_expiry()
    {
        var service = Service();

        var noReason = await service.GrantToOrganizationAsync(
            new OrganizationGrantCreditsRequest { MeterKey = "ai", Quantity = 25 }, "corr-1", CancellationToken.None);
        var granted = await service.GrantToOrganizationAsync(
            new OrganizationGrantCreditsRequest { MeterKey = "ai", Quantity = 25, Reason = "Support goodwill" },
            "corr-2", CancellationToken.None);

        noReason.FailureKind.Should().Be(PaymentFailureKind.Validation, "AC-19");
        granted.IsSuccess.Should().BeTrue(granted.ErrorMessage);
        granted.Value!.Source.Should().Be("AdminGranted");
        granted.Value.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30));
        _published.Should().Equal(SubscriptionConstants.OrganizationCreditsGranted);
    }

    [Fact]
    public async Task Listing_shows_only_the_organizations_own_grants_and_purchases()
    {
        _grants.All.Add(new SubscriptionCreditGrant
        {
            ItemId = "sub-grant", TenantId = TenantId, OrganizationId = OrganizationId, SubscriptionId = "sub-1",
            MeterKey = "ai", Quantity = 5, Remaining = 5, GrantedAtUtc = Now.UtcDateTime
        });
        var service = Service();
        await service.PurchaseForOrganizationAsync(Buy(key: "k1"), "corr-1", CancellationToken.None);

        var grants = await service.ListOrganizationGrantsAsync(null, null, null, "corr-2", CancellationToken.None);
        var purchases = await service.ListOrganizationPurchasesAsync(null, null, "corr-3", CancellationToken.None);

        grants.Value.Should().ContainSingle(grant => grant.Source == "Purchased",
            "a subscription's grant is not the organization's standalone credit");
        purchases.Value.Should().ContainSingle();
    }

    // ---- helpers ------------------------------------------------------------------------------

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
        new CreditLedger(
            _grants, Mock.Of<ISubscriptionUsageRepository>(), _subscriptions.Object, Mock.Of<ISubscriptionOutboxEventFactory>(),
            Mock.Of<ISubscriptionWorkScheduler>(), _offers, OrganizationOutbox()),
        _payments.Object,
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        _audit.Object,
        new CreditPurchaseRequestValidator(),
        new GrantCreditsRequestValidator(),
        new MoveCreditsRequestValidator(),
        NullLogger<SubscriptionCreditService>.Instance,
        _time,
        _paymentService.Object,
        new CurrencyMinorUnitResolver(Mock.Of<IOptionsMonitor<PaymentOptions>>(monitor => monitor.CurrentValue == new PaymentOptions())),
        new BillingAccountCardAdopter(_storedMethods.Object, _billingAccounts.Object, NullLogger<BillingAccountCardAdopter>.Instance),
        null,
        _offers,
        _catalogue,
        new OrganizationBillingAccountResolver(_billingAccounts.Object),
        new OrganizationCreditPurchaseRequestValidator(),
        new OrganizationGrantCreditsRequestValidator());

    private static OrganizationCreditPurchaseRequest Buy(
        decimal quantity = 100,
        string currency = "USD",
        string meterKey = "ai",
        string? code = null,
        string? key = null,
        string? pack = null) => new()
    {
        MeterKey = meterKey,
        Quantity = quantity,
        CurrencyCode = currency,
        DiscountCode = code,
        IdempotencyKey = key,
        PackCode = pack
    };

    private static CreditOffer Offer(string? organizationId = null, long usdUnitAmountMinor = 10) => new()
    {
        ItemId = organizationId is null ? "offer-tenant" : $"offer-{organizationId}",
        TenantId = TenantId,
        OrganizationId = organizationId,
        MeterKey = "ai",
        Enabled = true,
        UnitPrices =
        [
            new MeterTopUpPrice { CurrencyCode = "USD", UnitAmountMinor = usdUnitAmountMinor },
            new MeterTopUpPrice { CurrencyCode = "CHF", UnitAmountMinor = 12 }
        ],
        MinQuantity = 10,
        MaxQuantity = 1_000,
        ExpiresAfter = new CreditExpiryDuration { Count = 30, Unit = CreditExpiryUnit.Day },
        Version = 3
    };

    private OrganizationCreditOutboxProcessor OrganizationOutbox() => new(
        _grants, _messages.Object, new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<OrganizationCreditOutboxProcessor>.Instance);
}
