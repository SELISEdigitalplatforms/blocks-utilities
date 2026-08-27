using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Raising the first charge, and what happens when there is nothing to charge or the charge
/// declines.
/// </summary>
public sealed class SubscriptionCheckoutServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionCreationService> _creation = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionPaymentLinkRepository> _links = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IPaymentService> _payments = new();
    private readonly Mock<IPaymentMethodSetupService> _paymentMethodSetups = new();
    private readonly Mock<IPaymentRepository> _paymentRepository = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _currency = new();

    private SubscriptionDetail _subscription = NewSubscription();
    private MakePaymentRequest? _paymentRequest;
    private string? _idempotencyKey;
    private SubscriptionPaymentLink? _link;

    public SubscriptionCheckoutServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
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

        _currency
            .Setup(resolver => resolver.TryConvertBack(
                It.IsAny<long>(), It.IsAny<string>(), out It.Ref<decimal>.IsAny))
            .Returns((long minor, string _, out decimal amount) =>
            {
                amount = minor / 100m;

                return true;
            });

        _payments
            .Setup(service => service.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<MakePaymentRequest, string, string, CancellationToken>(
                (request, key, _, _) =>
                {
                    _paymentRequest = request;
                    _idempotencyKey = key;
                })
            .ReturnsAsync(PaymentOperationResult.Success(
                new PaymentResponse
                {
                    PaymentDetailId = "pay-1",
                    RedirectUrl = "https://checkout.stripe.com/session"
                },
                "corr-1"));

        _links
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionPaymentLink>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionPaymentLink, CancellationToken>((link, _) => _link = link)
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task The_charge_saves_the_payment_method_for_the_renewal()
    {
        await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        _paymentRequest!.SavePaymentMethod.Should().BeTrue(
            "the renewal charges this card with nobody present, which the provider only " +
            "permits if the mandate was taken when it was saved");
    }

    [Fact]
    public async Task The_initial_charge_is_attributed_to_the_subscriber_organization()
    {
        await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        _paymentRequest!.CustomerOrganizationId.Should().Be(OrganizationId);
    }

    [Fact]
    public async Task The_charge_carries_the_subscriptions_own_order_id_and_idempotency_key()
    {
        await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        _paymentRequest!.OrderId.Should().Be($"sub:{_subscription.ItemId}");

        // Asserted through the deriving function rather than as a literal: the key is a UUID
        // hashed from the subscription, and what matters is that checkout and the recovery sweep
        // derive the same one, not what it spells.
        _idempotencyKey.Should().Be(
            SubscriptionConstants.InitialChargeKeyFor(_subscription.ItemId),
            "a retried request must find the same payment, not raise a second one");
        Guid.TryParse(_idempotencyKey, out _).Should().BeTrue(
            "the payment module refuses an idempotency key that is not a UUID");
    }

    [Fact]
    public async Task The_charge_does_not_name_an_organization()
    {
        await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        _paymentRequest!.OrganizationId.Should().BeNull(
            "organizations here are subscribers, not merchants: naming one would look for a " +
            "merchant account the customer does not have");
    }

    [Fact]
    public async Task The_amount_is_the_quantity_times_the_snapshotted_price()
    {
        await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        _paymentRequest!.Amount.Should().Be(1068.00m);
    }

    [Fact]
    public async Task A_pending_link_records_which_payment_to_wait_for()
    {
        await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        _link!.PaymentDetailId.Should().Be("pay-1");
        _link.State.Should().Be(SubscriptionPaymentLinkState.Pending);
        _link.CorrelationId.Should().Be("corr-1",
            "the sweep that settles this runs later and elsewhere, so the trace has to travel " +
            "with the record");
    }

    [Fact]
    public async Task The_checkout_url_is_returned_to_the_caller()
    {
        var result = await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        result.Value!.CheckoutUrl.Should().Be("https://checkout.stripe.com/session");
        result.Value.Status.Should().Be(nameof(SubscriptionStatus.Incomplete));
    }

    [Fact]
    public async Task Retrying_the_same_incomplete_subscription_returns_its_existing_checkout()
    {
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
        ArrangePendingCheckout();

        var result = await Service().SubscribeAsync(
            MatchingRequest(), "corr-2", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SubscriptionId.Should().Be(_subscription.ItemId);
        result.Value.CheckoutUrl.Should().Be("https://checkout.stripe.com/existing");
        _payments.Verify(
            service => service.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "resuming must not create a second provider checkout");
    }

    [Fact]
    public async Task Different_terms_report_the_pending_checkout_and_how_to_recover()
    {
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
        ArrangePendingCheckout();

        var request = MatchingRequest();
        request.PriceId = "another-price";
        var result = await Service().SubscribeAsync(request, "corr-2", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_checkout_pending");
        result.ValidationErrors!["subscriptionId"].Should().ContainSingle(_subscription.ItemId);
        result.ValidationErrors["checkoutUrl"].Should()
            .ContainSingle("https://checkout.stripe.com/existing");
    }

    [Fact]
    public async Task A_declined_charge_leaves_the_subscription_granting_nothing()
    {
        _payments
            .Setup(service => service.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentOperationResult.Failure(
                PaymentFailureKind.ProviderRejected,
                "payment_refused",
                "The card was declined.",
                "corr-1"));

        var result = await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);

        _links.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionPaymentLink>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "there is no payment to wait for");

        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "nothing was granted, so nothing needs revoking");
    }

    [Fact]
    public async Task A_card_free_trial_starts_without_touching_the_money_path()
    {
        _subscription.Trial = new TrialTerms
        {
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddDays(14),
            RequiresPaymentMethod = false
        };

        var result = await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Trialing));

        _payments.Verify(
            service => service.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "a zero-amount charge is refused by the money path, so it must never be attempted");
    }

    [Fact]
    public async Task A_fully_discounted_period_starts_without_a_charge()
    {
        _subscription.Discount = new DiscountTerms
        {
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 10_000
        };

        var result = await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Active));

        _payments.Verify(
            service => service.MakePaymentAsync(
                It.IsAny<MakePaymentRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_caller_without_an_organization_never_reaches_creation()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Unavailable,
                "subscription_organization_missing",
                "An organization is required."));

        var result = await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_organization_missing");
        _creation.Verify(
            service => service.CreateAsync(
                It.IsAny<CreateSubscriptionRequest>(),
                It.IsAny<SubscriptionContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_requested_organization_is_forwarded_to_context_resolution()
    {
        await Service().SubscribeAsync(
            new CreateSubscriptionRequest { OrganizationId = "org-9" },
            "corr-1",
            CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    [Fact]
    public async Task A_requested_organization_on_get_current_is_forwarded_to_context_resolution()
    {
        await Service().GetCurrentAsync("org-9", "corr-1", CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync("corr-1", "org-9", It.IsAny<CancellationToken>()),
            Times.Once,
            "only the console gets to act on this, and that is decided downstream in " +
            "SubscriptionContextResolver — this only proves the value reaches it");
    }

    [Fact]
    public async Task Current_returns_an_incomplete_subscription_with_its_pending_checkout()
    {
        ArrangePendingCheckout();

        var result = await Service().GetCurrentAsync(null, "corr-2", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Incomplete));
        result.Value.SubscriptionId.Should().Be(_subscription.ItemId);
        result.Value.CheckoutUrl.Should().Be("https://checkout.stripe.com/existing");
    }

    [Fact]
    public async Task Current_prefers_a_live_subscription_over_any_pending_lookup()
    {
        _subscription.Status = SubscriptionStatus.Active;
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_subscription);

        var result = await Service().GetCurrentAsync(null, "corr-2", CancellationToken.None);

        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Active));
        result.Value.CheckoutUrl.Should().BeNull();
        _subscriptions.Verify(
            repository => repository.GetIncompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// An organization with no subscription is answered, not refused.
    /// </summary>
    /// <remarks>
    /// This used to be a 404, which says the endpoint is not there. A caller cannot tell that from a
    /// bad route, a revoked path or a typo, so reading an ordinary "not yet" meant special-casing one
    /// status code and hoping it never meant anything else.
    /// </remarks>
    [Fact]
    public async Task Current_answers_no_subscription_with_an_empty_success_rather_than_a_404()
    {
        // Neither lookup finds anything, which is every organization that has not subscribed.
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);
        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        var result = await Service().GetCurrentAsync(null, "corr-empty", CancellationToken.None);

        result.IsSuccess.Should().BeTrue("having no subscription is an answer, not a failure");
        result.Value.Should().BeNull("which is what renders as data: null");

        // No code and no kind, so nothing downstream can map this onto a status other than 200.
        result.ErrorCode.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
        result.FailureKind.Should().Be(default(PaymentFailureKind));
        result.CorrelationId.Should().Be("corr-empty");
    }

    /// <summary>
    /// A subscription with a scheduled cancellation is still live: it keeps granting until its
    /// current period ends, and <c>/current</c> is exactly the read that must keep saying so.
    /// </summary>
    [Fact]
    public async Task Current_maps_a_persisted_scheduled_cancellation()
    {
        _subscription.Status = SubscriptionStatus.Active;
        _subscription.CancelAtPeriodEnd = true;
        _subscription.CanCancelImmediately = true;
        _subscription.CanceledAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc);
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_subscription);

        var result = await Service().GetCurrentAsync(null, "corr-2", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Active),
            "it remains live until CurrentPeriodEndUtc, which is what makes it findable here at all");
        result.Value.Cancellation.Should().NotBeNull();
        result.Value.Cancellation!.State.Should().Be("Scheduled");
        result.Value.Cancellation.RequestedAtUtc.Should().Be(_subscription.CanceledAtUtc.Value);
    }

    private SubscriptionCheckoutService Service() => new(
        _creation.Object,
        _subscriptions.Object,
        _links.Object,
        _contextResolver.Object,
        new SubscriptionOutboxEventFactory(),
        new SubscriptionResponseMapper(),
        _payments.Object,
        _paymentMethodSetups.Object,
        _paymentRepository.Object,
        _currency.Object,
        NullLogger<SubscriptionCheckoutService>.Instance);

    private void ArrangePendingCheckout()
    {
        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_subscription);
        _links
            .Setup(repository => repository.FindBySubscriptionAsync(
                TenantId, _subscription.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionPaymentLink
            {
                SubscriptionId = _subscription.ItemId,
                PaymentDetailId = "pay-existing",
                State = SubscriptionPaymentLinkState.Pending
            });
        _paymentRepository
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "pay-existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-existing",
                RedirectUrl = "https://checkout.stripe.com/existing",
                ExpirationDate = DateTime.UtcNow.AddHours(1)
            });
    }

    private CreateSubscriptionRequest MatchingRequest() => new()
    {
        PlanCode = _subscription.Plan.Code,
        PriceId = _subscription.Price.PriceId,
        TimeZoneId = _subscription.FeeSchedule.TimeZoneId
    };

    private static SubscriptionDetail NewSubscription()
    {
        var id = Guid.NewGuid().ToString();

        return new SubscriptionDetail
        {
            ItemId = id,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            Status = SubscriptionStatus.Incomplete,
            CurrencyCode = "CHF",
            OrderId = $"sub:{id}",
            Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
            Price = new PriceSnapshot
            {
                PriceId = "price-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 8900,
                QuantityItemKey = "seat"
            },
            FeeSchedule = new BillingSchedule { TimeZoneId = "UTC" },
            QuantityItems =
            [
                new SubscriptionQuantityItem
                {
                    ItemKey = "seat",
                    UnitLabel = "seat",
                    Quantity = 12,
                    UnitAmountMinor = 8900
                }
            ]
        };
    }
}
