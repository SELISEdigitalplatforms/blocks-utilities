using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription.Offline;

/// <summary>
/// An operator activating a paid plan for a bank-transfer customer (spec 006).
/// </summary>
/// <remarks>
/// Guards the one route that grants a paid plan without taking money: that only the console can
/// use it, that it never reaches a card or a checkout, that the subscription is live at once, and
/// that the opening period leaves a record invoice behind for the customer's own billing.
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
    private bool _callerIsConsole = true;

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
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "operator-1", "Operator")
                {
                    CallerIsConsole = _callerIsConsole
                }));

        _creation
            .Setup(service => service.CreateOfflineAsync(
                It.IsAny<CreateSubscriptionRequest>(),
                It.IsAny<OfflinePricing>(),
                It.IsAny<SubscriptionContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
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
    public async Task A_console_operator_activates_a_paid_plan_with_no_card_and_no_checkout()
    {
        var result = await Service().ActivateOfflineAsync(Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(because: result.ErrorMessage);
        result.Value!.CheckoutUrl.Should().BeNull(because: "a bank-transfer customer is never sent to pay");
        result.Value.Status.Should().Be(nameof(SubscriptionStatus.Active));
        result.Value.CollectionMethod.Should().Be(nameof(SubscriptionCollectionMethod.OfflineInvoice));
        _transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Incomplete);
        _transition.NewStatus.Should().Be(
            SubscriptionStatus.Active,
            because: "the plan is switched on at once; payment arrives later by bank transfer");
        _payments.VerifyNoOtherCalls();
        _setups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_opening_period_records_its_invoice_in_the_activating_write()
    {
        await Service().ActivateOfflineAsync(Request(), "corr-1", CancellationToken.None);

        var source = _transition!.DocumentSource;

        source.Should().NotBeNull(
            because: "the customer is invoiced for the first period too, and nothing else would " +
                "ever rediscover an invoice no payment stands behind");
        source!.ChargeKind.Should().Be(SubscriptionChargeKind.Initial);
        source.PaymentDetailId.Should().BeNull();
        source.Amounts!.TotalMinor.Should().Be(8_900);
        source.InitiatedBy!.UserId.Should().Be(
            "operator-1",
            because: "the operator activated it, and the record should say who did");
        _documents.Verify(
            documents => documents.RequestPendingAsync(_subscription, "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_caller_who_is_not_the_console_cannot_activate_an_offline_subscription()
    {
        _callerIsConsole = false;

        var result = await Service().ActivateOfflineAsync(Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(
            "subscription_offline_console_only",
            because: "an organization admin reaching this route would otherwise put their own " +
                "organization on a paid plan without paying");
        _creation.Verify(
            service => service.CreateOfflineAsync(
                It.IsAny<CreateSubscriptionRequest>(),
                It.IsAny<OfflinePricing>(),
                It.IsAny<SubscriptionContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Contract_pricing_without_an_amount_is_refused_before_anything_is_created()
    {
        var request = Request();
        request.Pricing = new OfflinePricingRequest { Mode = OfflinePricingMode.Contract };

        var result = await Service().ActivateOfflineAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_request_invalid");
        _subscriptions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task No_price_pricing_is_refused_until_service_statements_exist()
    {
        var request = Request();
        request.Pricing = new OfflinePricingRequest { Mode = OfflinePricingMode.None };

        var result = await Service().ActivateOfflineAsync(request, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be(
            "subscription_offline_pricing_unsupported",
            because: "a no-price subscription would otherwise renew issuing nothing at all");
    }

    [Fact]
    public async Task An_offline_plan_with_a_trial_starts_trialing_and_owes_no_invoice_yet()
    {
        _subscription.Trial = new TrialTerms
        {
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddDays(14)
        };

        await Service().ActivateOfflineAsync(Request(), "corr-1", CancellationToken.None);

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
        documents: _documents.Object,
        offlineValidator: new ActivateOfflineSubscriptionRequestValidator(_currency.Object));

    private static ActivateOfflineSubscriptionRequest Request() => new()
    {
        OrganizationId = OrganizationId,
        PlanCode = "professional",
        PriceId = "price-monthly",
        TimeZoneId = "UTC",
        Pricing = new OfflinePricingRequest { Mode = OfflinePricingMode.Catalogue }
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
            OfflinePricing = new OfflinePricing { Mode = OfflinePricingMode.Catalogue },
            ActivatedBy = new FinancialDocumentPerson { UserId = "operator-1", Name = "Operator" },
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
