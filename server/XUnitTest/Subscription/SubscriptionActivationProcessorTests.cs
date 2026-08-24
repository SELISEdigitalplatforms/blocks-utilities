using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Carrying a payment outcome into the subscription waiting on it.
/// </summary>
/// <remarks>
/// The rule under test throughout: only a webhook activates. Anything else — an optimistic
/// status, a shopper returning from a redirect — can be produced without money having moved.
/// </remarks>
public sealed class SubscriptionActivationProcessorTests
{
    private const string TenantId = "tenant-1";

    /// <summary>The subscriber. Its own subscription and billing account are stamped with this.</summary>
    private const string OrganizationId = "org-1";

    /// <summary>
    /// The organization whose merchant configuration takes the money — the console's, for a
    /// subscription the console created on a customer's behalf.
    /// </summary>
    private const string MerchantOrganizationId = "default";

    private readonly Mock<ISubscriptionPaymentLinkRepository> _links = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IStoredPaymentMethodRepository> _storedMethods = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionTransition? _transition;

    public SubscriptionActivationProcessorTests()
    {
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewSubscription);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, transition, _) => _transition = transition)
            .ReturnsAsync(true);

        _links
            .Setup(repository => repository.TrySettleAsync(
                TenantId,
                "link-1",
                It.IsAny<SubscriptionPaymentLinkState>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task An_authorized_payment_without_a_webhook_does_not_activate()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: false);

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(0);
        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "a status without a webhook behind it is not evidence that money moved");
    }

    [Fact]
    public async Task A_confirmed_payment_activates_the_subscription()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        _transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Incomplete);
        _transition.NewStatus.Should().Be(SubscriptionStatus.Active);
        _transition.Event!.EventType.Should().Be(SubscriptionConstants.SubscriptionActivated);
    }

    [Fact]
    public async Task A_confirmed_payment_on_a_trial_starts_the_trial_instead()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Captured, webhookConfirmed: true);

        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var subscription = NewSubscription();
                subscription.Trial = new TrialTerms
                {
                    StartsAtUtc = DateTime.UtcNow,
                    EndsAtUtc = DateTime.UtcNow.AddDays(14)
                };

                return subscription;
            });

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.Trialing);
    }

    [Fact]
    public async Task A_refused_payment_ends_the_subscription()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Refused, webhookConfirmed: true);

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.IncompleteExpired);
        _transition.ClearNextFeeBillingAt.Should().BeTrue();

        _links.Verify(
            repository => repository.TrySettleAsync(
                TenantId,
                "link-1",
                SubscriptionPaymentLinkState.Abandoned,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_subscription_already_carried_across_settles_without_transitioning_again()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);

        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var subscription = NewSubscription();
                subscription.Status = SubscriptionStatus.Active;

                return subscription;
            });

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Losing_the_transition_race_leaves_the_link_for_the_winner()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(0);
        _links.Verify(
            repository => repository.TrySettleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                SubscriptionPaymentLinkState.Applied,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_pending_payment_is_rescheduled_rather_than_abandoned()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Processing, webhookConfirmed: false);

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _links.Verify(
            repository => repository.RescheduleAsync(
                TenantId,
                "link-1",
                1,
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The regression this guards: adoption read <c>payment.StoredPaymentMethodPublicId</c>, which
    /// only a charge made <em>from</em> a stored card ever carries. Hosted checkout never writes
    /// it, so every subscription reached its first renewal with no card on the billing account and
    /// failed closed — a whole billing period after the mistake, with nothing logged at the time.
    /// </summary>
    [Fact]
    public async Task The_provider_customer_is_taken_from_the_card_the_charge_saved()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSavedCard();

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _accounts.Verify(
            repository => repository.TrySetProviderCustomerAsync(
                TenantId,
                "acct-1",
                "cus_123",
                "method-1",
                MerchantOrganizationId,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the renewal needs this identifier, and checkout is the only place it appears");
    }

    /// <summary>
    /// Organizations here are subscribers, not merchants: a tenant configures one provider and
    /// every organization is charged through it. So the scope recorded for later charges is the
    /// one that took the money, which for a console-created subscription is not the subscriber's.
    /// </summary>
    [Fact]
    public async Task The_merchants_organization_is_recorded_rather_than_the_subscribers()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSavedCard();

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _accounts.Verify(
            repository => repository.TrySetProviderCustomerAsync(
                TenantId,
                "acct-1",
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.Is<string?>(organizationId => organizationId != OrganizationId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "recording the subscriber's organization would send every renewal looking for a " +
            "merchant account the customer does not have");
    }

    [Fact]
    public async Task A_paid_subscription_with_no_card_to_renew_on_says_so()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);

        // No saved card: ListActiveAsync is unmocked and returns none.
        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _accounts.Verify(
            repository => repository.TrySetProviderCustomerAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "there is nothing to record, and the activation still stands — the customer paid");
    }

    [Fact]
    public async Task A_charge_raised_but_never_recorded_is_recovered_by_its_derived_key()
    {
        _subscriptions
            .Setup(repository => repository.ListStaleAsync(
                TenantId,
                SubscriptionStatus.Incomplete,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewSubscription()]);

        _links
            .Setup(repository => repository.FindBySubscriptionAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionPaymentLink?)null);

        _payments
            .Setup(repository => repository.GetByIdempotencyKeyAsync(
                TenantId,
                SubscriptionConstants.InitialChargeKeyFor("sub-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail { ItemId = "pay-1" });

        var recovered = await Processor().RecoverStaleAsync(TenantId, CancellationToken.None);

        recovered.Should().Be(1);
        _links.Verify(
            repository => repository.TryCreateAsync(
                It.Is<SubscriptionPaymentLink>(link => link.PaymentDetailId == "pay-1"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "otherwise the customer has paid and the subscription grants nothing, with " +
            "nothing scanning for it");
    }

    [Fact]
    public async Task A_stale_subscription_with_no_charge_at_all_is_expired()
    {
        _subscriptions
            .Setup(repository => repository.ListStaleAsync(
                TenantId,
                SubscriptionStatus.Incomplete,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewSubscription()]);

        _links
            .Setup(repository => repository.FindBySubscriptionAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionPaymentLink?)null);

        _payments
            .Setup(repository => repository.GetByIdempotencyKeyAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        await Processor().RecoverStaleAsync(TenantId, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.IncompleteExpired);
    }

    private void GivenDueLink() =>
        _links
            .Setup(repository => repository.ListDueAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubscriptionPaymentLink
                {
                    ItemId = "link-1",
                    TenantId = TenantId,
                    OrganizationId = "org-1",
                    SubscriptionId = "sub-1",
                    PaymentDetailId = "pay-1",
                    CorrelationId = "corr-1"
                }
            ]);

    private void GivenPayment(
        string status,
        bool webhookConfirmed,
        string? storedMethodId = null,
        string? shopperReference = "shopper-1",
        string paymentOrganizationId = MerchantOrganizationId) =>
        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-1",
                TenantId = TenantId,
                PaymentStatus = status,
                WebhookConfirmedAtUtc = webhookConfirmed ? DateTime.UtcNow : null,
                StoredPaymentMethodPublicId = storedMethodId,
                ShopperReference = shopperReference,
                // The console's organization, not the subscriber's: this is what a
                // console-created subscription actually pays under.
                OrganizationId = paymentOrganizationId
            });

    /// <summary>The card is found under the reference it was saved with, at that organization.</summary>
    private void GivenSavedCard(
        string customerId = "cus_123",
        string methodId = "method-1",
        string shopperReference = "shopper-1",
        string organizationId = MerchantOrganizationId) =>
        _storedMethods
            .Setup(repository => repository.ListActiveAsync(
                TenantId,
                It.Is<IReadOnlyCollection<StoredPaymentMethodLookupScope>>(scopes =>
                    scopes.Any(scope =>
                        scope.ShopperReference == shopperReference &&
                        scope.OrganizationId == organizationId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StoredPaymentMethod
                {
                    ItemId = methodId,
                    ProviderPayerReference = customerId,
                    CreatedAtUtc = DateTime.UtcNow
                }
            ]);

    private SubscriptionActivationProcessor Processor() => new(
        _links.Object,
        _subscriptions.Object,
        _accounts.Object,
        new SubscriptionOutboxEventFactory(),
        _payments.Object,
        _storedMethods.Object,
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<SubscriptionActivationProcessor>.Instance,
        _time);

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = "org-1",
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Incomplete,
        CurrencyCode = "CHF",
        OrderId = "sub:sub-1",
        CorrelationId = "corr-1",
        Plan = new PlanSnapshot { Code = "professional" }
    };

}
