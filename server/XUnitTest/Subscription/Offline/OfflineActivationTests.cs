using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription.Offline;

/// <summary>
/// The ordinary subscribe with <c>offline: true</c>.
/// </summary>
/// <remarks>
/// Guards the route that grants a paid plan without taking money: that it never reaches a card or
/// a checkout, that the subscription is live at once, and that the opening period leaves a record
/// invoice behind. The permission, not the service, is the gate.
/// </remarks>
public sealed class OfflineActivationTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "customer-org";

    private readonly Mock<ISubscriptionCreationService> _creation = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IPaymentService> _payments = new(MockBehavior.Strict);
    private readonly Mock<IPaymentMethodSetupService> _setups = new(MockBehavior.Strict);
    private readonly Mock<ICurrencyMinorUnitResolver> _currency = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionFinancialDocumentAnnouncer> _documents = new();

    private SubscriptionDetail _subscription = OfflineSubscription();
    private SubscriptionTransition? _transition;

    public OfflineActivationTests()
    {
        decimal ignored;
        _currency
            .Setup(currency => currency.TryConvertBack(It.IsAny<long>(), "CHF", out ignored))
            .Returns(true);

        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "operator-1", "Operator")));

        _creation
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateSubscriptionRequest>(),
                It.IsAny<SubscriptionContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(() => SubscriptionOperationResult<SubscriptionDetail>.Success(
                _subscription, "corr-1"));

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, transition, _) => _transition = transition)
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task An_offline_activation_is_live_at_once_and_returns_no_checkout_url()
    {
        var result = await Subscribe(offline: true);

        result.IsSuccess.Should().BeTrue(because: result.ErrorMessage);
        result.Value!.CheckoutUrl.Should().BeNull(because: "a bank-transfer customer is never sent to pay");
        result.Value.CollectionMethod.Should().Be(nameof(SubscriptionCollectionMethod.OfflineInvoice));
        _transition!.NewStatus.Should().Be(
            SubscriptionStatus.Active,
            because: "the plan is switched on at once; payment arrives later by bank transfer");
    }

    [Fact]
    public async Task An_offline_activation_never_reaches_the_gateway()
    {
        ChargeableCardAccount();

        var result = await Subscribe(offline: true);

        result.IsSuccess.Should().BeTrue(because: result.ErrorMessage);
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Active);

        _payments.VerifyNoOtherCalls();
        _setups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_offline_activation_never_opens_a_card_setup_session()
    {
        _subscription.InitialChargeAmountMinor = 0;
        _subscription.Plan.RequirePaymentMethodUpfront = true;
        SubscriptionAmountCalculator.RequiresCardSetup(_subscription).Should().BeTrue(
            because: "the premise: this subscription would ask a card customer for a card");

        var result = await Subscribe(offline: true);

        result.IsSuccess.Should().BeTrue(because: result.ErrorMessage);
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Active);
        _setups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_offline_opening_period_leaves_an_unpaid_record_invoice_source()
    {
        await Subscribe(offline: true);

        var source = _transition!.DocumentSource;

        source.Should().NotBeNull(
            because: "the customer is invoiced for the first period too, and nothing else would " +
                "ever rediscover an invoice no payment stands behind");
        source!.DocumentType.Should().Be(FinancialDocumentType.Invoice);
        source.PaymentDetailId.Should().BeNull();
        source.Amounts!.TotalMinor.Should().Be(8_900);
    }

    [Fact]
    public async Task An_offline_activation_is_never_resumed_into_a_card_checkout()
    {
        _creation
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateSubscriptionRequest>(),
                It.IsAny<SubscriptionContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(SubscriptionOperationResult<SubscriptionDetail>.Failure(
                PaymentFailureKind.Validation,
                "subscription_already_active",
                "Already active.",
                "corr-1"));

        var result = await Subscribe(offline: true);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_already_active");
        result.Value.Should().BeNull(because: "the offline route never returns a checkout");
        _payments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_card_activation_still_goes_to_checkout()
    {
        _subscription.CollectionMethod = SubscriptionCollectionMethod.AutomaticCharge;
        ChargeableCardAccount();
        _payments
            .Setup(payments => payments.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse
                {
                    PaymentDetailId = "pay-1",
                    RedirectUrl = "https://pay.example/checkout"
                },
                "corr-1"));

        var result = await Subscribe(offline: false);

        result.Value!.CheckoutUrl.Should().NotBeNull();
        _payments.Verify(
            payments => payments.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_offline_plan_with_a_trial_starts_trialing_and_owes_no_invoice_yet()
    {
        _subscription.Trial = new TrialTerms
        {
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddDays(14)
        };

        await Subscribe(offline: true);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.Trialing);
        _transition.DocumentSource.Should().BeNull(
            because: "a trial is invoiced at its conversion, not at its start");
    }

    [Fact]
    public async Task An_offline_subscription_does_not_take_a_payment_method()
    {
        _subscription.Status = SubscriptionStatus.Active;
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(TenantId, _subscription.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_subscription);

        var result = await Service().StartPaymentMethodSetupAsync(
            _subscription.ItemId, null, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be(
            "subscription_offline_collection",
            because: "a card added to a bank-transfer contract is never charged, so offering to " +
                "take one tells the customer something untrue");
        _setups.VerifyNoOtherCalls();
    }

    // What a card charge needs before it can reach the (strict) gateway mock: with this in place, a
    // wrongly taken card path trips _payments instead of failing early on a missing account.
    private void ChargeableCardAccount()
    {
        _billingAccounts
            .Setup(repository => repository.GetAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount { ProviderName = PaymentConstants.StripeProvider });
        decimal ignored;
        _currency
            .Setup(currency => currency.TryConvertBack(8_900, "CHF", out ignored))
            .Returns(true);
    }

    private Task<SubscriptionOperationResult<SubscriptionResponse>> Subscribe(bool offline) =>
        Service().SubscribeAsync(Request(), "corr-1", CancellationToken.None, offline);

    private SubscriptionCheckoutService Service() => new(
        _creation.Object,
        _subscriptions.Object,
        Mock.Of<ISubscriptionPaymentLinkRepository>(),
        _contextResolver.Object,
        new SubscriptionOutboxEventFactory(),
        new SubscriptionResponseMapper(),
        _payments.Object,
        _setups.Object,
        Mock.Of<IPaymentRepository>(),
        _currency.Object,
        _billingAccounts.Object,
        NullLogger<SubscriptionCheckoutService>.Instance,
        documents: _documents.Object);

    private static CreateSubscriptionRequest Request() => new()
    {
        OrganizationId = OrganizationId,
        PlanCode = "professional",
        PriceId = "price-monthly",
        TimeZoneId = "UTC"
    };

    private static SubscriptionDetail OfflineSubscription()
    {
        var id = Guid.NewGuid().ToString();

        return new SubscriptionDetail
        {
            ItemId = id,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            Status = SubscriptionStatus.Incomplete,
            CollectionMethod = SubscriptionCollectionMethod.OfflineInvoice,
            PurchasedBy = new FinancialDocumentPerson { UserId = "operator-1", Name = "Operator" },
            CurrencyCode = "CHF",
            OrderId = SubscriptionConstants.OrderIdFor(id),
            Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
            Price = new PriceSnapshot
            {
                PriceId = "price-monthly",
                CurrencyCode = "CHF",
                UnitAmountMinor = 8_900
            },
            FeeSchedule = new BillingSchedule
            {
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                AnchorInstantUtc = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
                TimeZoneId = "UTC",
                AnchorDayOfMonth = 5
            },
            InitialChargeAmountMinor = 8_900
        };
    }
}
