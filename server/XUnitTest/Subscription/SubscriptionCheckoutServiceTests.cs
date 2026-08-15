using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;

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
    public async Task The_charge_carries_the_subscriptions_own_order_id_and_idempotency_key()
    {
        await Service().SubscribeAsync(
            new CreateSubscriptionRequest(), "corr-1", CancellationToken.None);

        _paymentRequest!.OrderId.Should().Be($"sub:{_subscription.ItemId}");
        _idempotencyKey.Should().Be($"sub-init:{_subscription.ItemId}",
            "a retried request must find the same payment, not raise a second one");
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

    private SubscriptionCheckoutService Service() => new(
        _creation.Object,
        _subscriptions.Object,
        _links.Object,
        _contextResolver.Object,
        new SubscriptionOutboxEventFactory(),
        new SubscriptionResponseMapper(),
        _payments.Object,
        _currency.Object,
        NullLogger<SubscriptionCheckoutService>.Instance);

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
                CurrencyCode = "CHF",
                UnitAmountMinor = 8900,
                QuantityItemKey = "seat"
            },
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
