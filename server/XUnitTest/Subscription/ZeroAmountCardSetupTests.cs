using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Signing up for something that costs nothing today, on a plan that still wants a card.
/// </summary>
/// <remarks>
/// The two questions this separates — is anything payable, and must a card be on file — used to
/// have one answer, because the only way to hold a card was to charge it. So the interesting
/// cases here are all about a zero opening amount: whether it starts the subscription outright,
/// sends the subscriber to a card form, or does neither because the plan said nothing.
/// </remarks>
public sealed class ZeroAmountCardSetupTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SetupUrl = "https://checkout.stripe.com/setup";

    private readonly Mock<ISubscriptionCreationService> _creation = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionPaymentLinkRepository> _links = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IPaymentService> _payments = new();
    private readonly Mock<IPaymentMethodSetupService> _setups = new();
    private readonly Mock<IPaymentRepository> _paymentRepository = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _currency = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();

    private readonly SubscriptionDetail _subscription = FreeSubscription();

    private CreatePaymentMethodSetupRequest? _setupRequest;
    private string? _setupKey;
    private SubscriptionPaymentLink? _createdLink;
    private SubscriptionTransition? _transition;

    public ZeroAmountCardSetupTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _creation
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateSubscriptionRequest>(),
                It.IsAny<SubscriptionContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SubscriptionOperationResult<SubscriptionDetail>.Success(
                _subscription,
                "corr-1"));

        _setups
            .Setup(service => service.CreateSetupAsync(
                It.IsAny<CreatePaymentMethodSetupRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreatePaymentMethodSetupRequest, string, string, CancellationToken>(
                (request, key, _, _) =>
                {
                    _setupRequest = request;
                    _setupKey = key;
                })
            .ReturnsAsync((CreatePaymentMethodSetupRequest request, string _, string _, CancellationToken _) =>
                PaymentOperationResult.Success(
                    new PaymentResponse
                    {
                        PaymentDetailId = "setup-1",
                        RedirectUrl = SetupUrl,
                        OrganizationId = request.OrganizationId
                    },
                    "corr-1"));

        _links
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionPaymentLink>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionPaymentLink, CancellationToken>(
                (link, _) => _createdLink = link)
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, transition, _) => _transition = transition)
            .ReturnsAsync(true);

        // The billing account every test gets unless it sets up its own -- see the equivalent
        // default in SubscriptionCheckoutServiceTests.
        _billingAccounts
            .Setup(repository => repository.GetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount { ProviderName = PaymentConstants.StripeProvider });
    }

    [Fact]
    public async Task A_free_plan_that_asks_for_nothing_starts_at_once()
    {
        var result = await Subscribe();

        result.IsSuccess.Should().BeTrue();
        result.Value!.CheckoutUrl.Should().BeNull();
        _subscription.Status.Should().Be(SubscriptionStatus.Active);
        _setups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_free_plan_that_wants_a_card_sends_the_subscriber_to_collect_one()
    {
        _subscription.Plan.RequirePaymentMethodUpfront = true;

        var result = await Subscribe();

        result.IsSuccess.Should().BeTrue();
        result.Value!.CheckoutUrl.Should().Be(SetupUrl);
        result.Value.Status.Should().Be(nameof(SubscriptionStatus.Incomplete),
            "a card that has not been stored yet grants nothing");
        _transition.Should().BeNull(
            "activation waits for the provider, exactly as it does when money is taken");
    }

    /// <summary>
    /// A fully discounted opening period is a zero amount like any other, and follows whatever the
    /// plan configured rather than a rule of its own.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_fully_discounted_first_period_follows_the_plan(bool requireCard)
    {
        _subscription.Price.UnitAmountMinor = 8_900;
        _subscription.Discount = new DiscountTerms
        {
            Code = "founders",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 10_000
        };
        _subscription.InitialChargeAmountMinor = 0;
        _subscription.Plan.RequirePaymentMethodUpfront = requireCard;

        var result = await Subscribe();

        result.Value!.CheckoutUrl.Should().Be(requireCard ? SetupUrl : null);
    }

    /// <summary>
    /// The setting a trial has always had, honoured without charging for a period to honour it.
    /// </summary>
    [Fact]
    public async Task A_trial_that_requires_a_card_collects_one_without_taking_money()
    {
        _subscription.Trial = Trial(requiresPaymentMethod: true);

        var result = await Subscribe();

        result.Value!.CheckoutUrl.Should().Be(SetupUrl);
        _payments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_card_free_trial_still_starts_without_one()
    {
        _subscription.Trial = Trial(requiresPaymentMethod: false);

        var result = await Subscribe();

        result.Value!.CheckoutUrl.Should().BeNull();
        _subscription.Status.Should().Be(SubscriptionStatus.Trialing);
    }

    /// <summary>
    /// The combination the setting was asked for: free until the trial ends, with a card on file
    /// so the charge that ends it has something to bill.
    /// </summary>
    [Fact]
    public async Task A_card_free_trial_on_a_plan_that_demands_a_card_collects_one()
    {
        _subscription.Trial = Trial(requiresPaymentMethod: false);
        _subscription.Plan.RequirePaymentMethodUpfront = true;

        var result = await Subscribe();

        result.Value!.CheckoutUrl.Should().Be(SetupUrl);
    }

    [Fact]
    public async Task The_setup_is_linked_to_the_subscription_as_a_setup_rather_than_a_charge()
    {
        _subscription.Plan.RequirePaymentMethodUpfront = true;

        await Subscribe();

        _createdLink!.Purpose.Should().Be(SubscriptionPaymentPurpose.PaymentMethodSetup,
            "the sweep treats a failed setup differently from a declined charge, and the " +
            "purpose is how it tells them apart");
        _createdLink.PaymentDetailId.Should().Be("setup-1");
        _setupRequest!.CurrencyCode.Should().Be("CHF");
        _setupRequest.CustomerOrganizationId.Should().Be(OrganizationId);
        _setupKey.Should().Be(
            SubscriptionConstants.PaymentMethodSetupKeyFor(_subscription.ItemId, 0));
    }

    /// <summary>
    /// The card-setup session carries the billing profile's own email, so Stripe's page opens
    /// with it already filled in rather than asking the subscriber to type an address the
    /// billing profile collected a step earlier.
    /// </summary>
    [Fact]
    public async Task The_setup_session_carries_the_billing_email_for_stripe_to_prefill()
    {
        _subscription.Plan.RequirePaymentMethodUpfront = true;
        _billingAccounts
            .Setup(repository => repository.GetAsync(
                TenantId, _subscription.BillingAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                ProviderName = PaymentConstants.StripeProvider,
                BillingEmail = "maya@example.com"
            });

        await Subscribe();

        _setupRequest!.CustomerEmail.Should().Be("maya@example.com");
    }

    [Fact]
    public async Task A_provider_that_cannot_collect_a_card_leaves_the_subscription_recoverable()
    {
        _subscription.Plan.RequirePaymentMethodUpfront = true;
        _setups
            .Setup(service => service.CreateSetupAsync(
                It.IsAny<CreatePaymentMethodSetupRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Failure(
                PaymentFailureKind.Validation,
                "payment_method_setup_unsupported",
                "This payment provider cannot store a card without charging it.",
                "corr-1"));

        var result = await Subscribe();

        result.ErrorCode.Should().Be("payment_method_setup_unsupported");
        _transition.Should().BeNull(
            "nothing was granted and nothing was ended: no money moved, so the subscription " +
            "stays where it is and another attempt is free to succeed");
    }

    [Fact]
    public async Task A_live_setup_session_is_returned_again_rather_than_replaced()
    {
        ArrangeReturningSubscriber(expired: false);

        var result = await Subscribe(MatchingRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value!.CheckoutUrl.Should().Be("https://checkout.stripe.com/open");
        _setups.VerifyNoOtherCalls();
    }

    /// <summary>
    /// An expired card form is not a dead end. Nothing was paid, so there is nothing to reconcile
    /// and no reason to make somebody cancel and start over — but the session itself cannot be
    /// reopened, so the retry has to be a new one under a new key.
    /// </summary>
    [Fact]
    public async Task An_expired_setup_session_is_replaced_by_a_fresh_attempt()
    {
        ArrangeReturningSubscriber(expired: true);
        _subscriptions
            .Setup(repository => repository.TryBumpPaymentMethodSetupAttemptAsync(
                TenantId, _subscription.ItemId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Subscribe(MatchingRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value!.CheckoutUrl.Should().Be(SetupUrl);
        _setupKey.Should().Be(
            SubscriptionConstants.PaymentMethodSetupKeyFor(_subscription.ItemId, 1),
            "the provider would replay the expired session under the key that opened it");
        _links.Verify(
            repository => repository.TrySettleAsync(
                TenantId,
                "link-1",
                SubscriptionPaymentLinkState.Abandoned,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a pending link is what the activation sweep keeps coming back to");
    }

    [Fact]
    public async Task Two_retries_at_once_produce_one_new_session()
    {
        ArrangeReturningSubscriber(expired: true);
        _subscriptions
            .Setup(repository => repository.TryBumpPaymentMethodSetupAttemptAsync(
                TenantId, _subscription.ItemId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Subscribe(MatchingRequest());

        result.ErrorCode.Should().Be("subscription_checkout_pending");
        _setups.VerifyNoOtherCalls();
    }

    /// <summary>
    /// The rule an expired *charge* keeps: raising a second one is how the same money gets taken
    /// twice, so the caller is told to finish or cancel the first.
    /// </summary>
    [Fact]
    public async Task An_expired_charge_is_still_a_conflict()
    {
        ArrangeReturningSubscriber(expired: true, purpose: SubscriptionPaymentPurpose.InitialCharge);

        var result = await Subscribe(MatchingRequest());

        result.ErrorCode.Should().Be("subscription_checkout_pending");
        _setups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_pending_setup_is_reported_by_the_current_subscription_endpoint()
    {
        ArrangeReturningSubscriber(expired: false);
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        var result = await Service().GetCurrentAsync(null, "corr-2", CancellationToken.None);

        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Incomplete));
        result.Value.CheckoutUrl.Should().Be("https://checkout.stripe.com/open");
        result.Value.PendingCheckout!.State.Should().Be("Pending");
        result.Value.PendingCheckout.Purpose.Should().Be("PaymentMethodSetup");
    }

    [Fact]
    public async Task A_failed_setup_is_explicit_on_the_current_subscription_endpoint()
    {
        ArrangeReturningSubscriber(expired: false, paymentStatus: PaymentStatuses.Refused);
        _subscriptions.Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        var result = await Service().GetCurrentAsync(null, "corr-2", CancellationToken.None);

        result.Value!.PendingCheckout!.State.Should().Be("Failed");
        result.Value.PendingCheckout.ErrorCode.Should().Be("payment_method_setup_failed");
        result.Value.CheckoutUrl.Should().BeNull();
    }

    [Fact]
    public async Task An_expired_setup_is_explicit_on_the_current_subscription_endpoint()
    {
        ArrangeReturningSubscriber(expired: true);
        _subscriptions.Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        var result = await Service().GetCurrentAsync(null, "corr-2", CancellationToken.None);

        result.Value!.PendingCheckout!.State.Should().Be("Expired");
        result.Value.PendingCheckout.ErrorCode.Should().Be("payment_method_setup_expired");
        result.Value.CheckoutUrl.Should().BeNull();
    }

    private Task<SubscriptionOperationResult<global::Subscription.DomainService.Responses.SubscriptionResponse>> Subscribe(
        CreateSubscriptionRequest? request = null) =>
        Service().SubscribeAsync(
            request ?? new CreateSubscriptionRequest(),
            "corr-1",
            CancellationToken.None);

    /// <summary>
    /// A second signup request from an organization that already holds an incomplete attempt —
    /// the shape a browser produces when somebody reloads the page.
    /// </summary>
    private void ArrangeReturningSubscriber(
        bool expired,
        SubscriptionPaymentPurpose purpose = SubscriptionPaymentPurpose.PaymentMethodSetup,
        string paymentStatus = PaymentStatuses.Processing)
    {
        _subscription.Plan.RequirePaymentMethodUpfront = true;

        _creation
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateSubscriptionRequest>(),
                It.IsAny<SubscriptionContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<SubscriptionDetail>.Failure(
                PaymentFailureKind.Conflict,
                "subscription_already_active",
                "This organization already has a live subscription.",
                "corr-1"));

        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_subscription);

        _links
            .Setup(repository => repository.FindBySubscriptionAsync(
                TenantId, _subscription.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionPaymentLink
            {
                ItemId = "link-1",
                TenantId = TenantId,
                SubscriptionId = _subscription.ItemId,
                PaymentDetailId = "setup-open",
                Purpose = purpose,
                State = SubscriptionPaymentLinkState.Pending
            });

        _paymentRepository
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "setup-open", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "setup-open",
                PaymentStatus = paymentStatus,
                RedirectUrl = "https://checkout.stripe.com/open",
                ExpirationDate = expired
                    ? DateTime.UtcNow.AddHours(-1)
                    : DateTime.UtcNow.AddHours(1)
            });

        _links
            .Setup(repository => repository.TrySettleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionPaymentLinkState>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private CreateSubscriptionRequest MatchingRequest() => new()
    {
        PlanCode = _subscription.Plan.Code,
        PriceId = _subscription.Price.PriceId,
        TimeZoneId = _subscription.FeeSchedule.TimeZoneId
    };

    private SubscriptionCheckoutService Service() => new(
        _creation.Object,
        _subscriptions.Object,
        _links.Object,
        _contextResolver.Object,
        new SubscriptionOutboxEventFactory(),
        new SubscriptionResponseMapper(),
        _payments.Object,
        _setups.Object,
        _paymentRepository.Object,
        _currency.Object,
        _billingAccounts.Object,
        NullLogger<SubscriptionCheckoutService>.Instance);

    private static TrialTerms Trial(bool requiresPaymentMethod) => new()
    {
        StartsAtUtc = DateTime.UtcNow,
        EndsAtUtc = DateTime.UtcNow.AddDays(14),
        RequiresPaymentMethod = requiresPaymentMethod
    };

    /// <summary>A plan priced at nothing, which is the only case any of this is about.</summary>
    private static SubscriptionDetail FreeSubscription()
    {
        var id = Guid.NewGuid().ToString();

        return new SubscriptionDetail
        {
            ItemId = id,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            Status = SubscriptionStatus.Incomplete,
            CurrencyCode = "CHF",
            OrderId = SubscriptionConstants.OrderIdFor(id),
            Plan = new PlanSnapshot { Code = "community", DisplayName = "Community" },
            Price = new PriceSnapshot
            {
                PriceId = "price-free",
                CurrencyCode = "CHF",
                UnitAmountMinor = 0
            },
            FeeSchedule = new BillingSchedule { TimeZoneId = "UTC" },
            InitialChargeAmountMinor = 0
        };
    }
}
