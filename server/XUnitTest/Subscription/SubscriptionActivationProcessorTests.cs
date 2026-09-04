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
using Subscription.DomainService.Services;
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
    private readonly Mock<ISubscriptionFinancialDocumentAnnouncer> _documents = new();
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

    private void GivenDueLink(
        SubscriptionPaymentPurpose purpose = SubscriptionPaymentPurpose.InitialCharge) =>
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
                    Purpose = purpose,
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

    /// <summary>
    /// A paid stub consumes one period of a limited promotion, read from what checkout froze.
    /// </summary>
    /// <remarks>
    /// The promotion here expired between the charge being raised and this activation settling it,
    /// which is exactly the case that must not be re-evaluated: the money already taken was reduced
    /// by the discount, so the period is spent whatever the clock now says.
    /// </remarks>
    [Fact]
    public async Task A_paid_stub_consumes_a_discount_period_even_once_the_promotion_has_lapsed()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSubscription(subscription =>
        {
            subscription.Price = CalendarMonthly();
            subscription.InitialChargeAmountMinor = 1_608;
            subscription.InitialChargeProrated = true;
            subscription.InitialChargeDiscountApplied = true;
            subscription.ProrationDays = 7;
            subscription.ProrationTotalDays = 31;
            subscription.Discount = new DiscountTerms
            {
                Code = "welcome",
                Kind = DiscountKind.Percent,
                PercentBasisPoints = 2_000,
                DurationPeriods = 3,
                // Lapsed before the webhook arrived.
                ExpiresAtUtc = _time.GetUtcNow().UtcDateTime.AddHours(-1)
            };
        });

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition!.DiscountPeriodsApplied.Should().Be(1,
            "the charge that was taken was discounted, so one of the three periods is spent");
    }

    [Fact]
    public async Task A_stub_that_no_promotion_reduced_spends_nothing()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSubscription(subscription =>
        {
            subscription.Price = CalendarMonthly();
            subscription.InitialChargeAmountMinor = 2_010;
            subscription.InitialChargeProrated = true;
            subscription.InitialChargeDiscountApplied = false;
        });

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition!.DiscountPeriodsApplied.Should().BeNull();
    }

    /// <summary>
    /// An anniversary first period deliberately still counts for nothing here — changing that
    /// would shorten every existing plan's discount for reasons unrelated to calendar billing.
    /// </summary>
    [Fact]
    public async Task An_anniversary_first_period_does_not_spend_a_discount_period()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSubscription(subscription =>
        {
            subscription.InitialChargeAmountMinor = 7_120;
            subscription.InitialChargeProrated = false;
            subscription.InitialChargeDiscountApplied = true;
        });

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition!.DiscountPeriodsApplied.Should().BeNull();
    }

    /// <summary>
    /// A calendar-aligned signup on the first buys a whole period and is charged for it. A
    /// promotion that reduced that charge has been used, and a one-period promotion escaping the
    /// count here would go on to discount a second payment.
    /// </summary>
    [Fact]
    public async Task A_calendar_first_period_spends_one_even_when_it_is_whole()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSubscription(subscription =>
        {
            subscription.Price = CalendarMonthly();
            subscription.InitialChargeAmountMinor = 7_120;
            subscription.InitialChargeProrated = false;
            subscription.InitialChargeDiscountApplied = true;
        });

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition!.DiscountPeriodsApplied.Should().Be(1);
    }

    /// <summary>A calendar-aligned monthly price, which is what a stub can only arise on.</summary>
    private static PriceSnapshot CalendarMonthly() => new()
    {
        CurrencyCode = "CHF",
        UnitAmountMinor = 8_900,
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        BillingAlignment = BillingAlignment.CalendarMonth
    };

    private void GivenSubscription(Action<SubscriptionDetail> configure) =>
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var subscription = NewSubscription();
                configure(subscription);

                return subscription;
            });

    /// <summary>
    /// A card confirmed for an Unpaid subscription triggers the recovery charge immediately.
    /// </summary>
    /// <remarks>
    /// The card just adopted is the one thing an Unpaid subscription was missing, so this is the
    /// moment recovery becomes possible -- not a moment later, and not left for a sweep that
    /// (correctly) never looks at an Unpaid subscription on its own initiative.
    /// </remarks>
    [Fact]
    public async Task A_card_confirmed_for_an_unpaid_subscription_triggers_recovery()
    {
        GivenDueLink(SubscriptionPaymentPurpose.PaymentMethodSetup);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSavedCard();
        GivenSubscription(subscription => subscription.Status = SubscriptionStatus.Unpaid);

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        _renewals.Verify(
            service => service.RecoverAsync(
                It.Is<SubscriptionDetail>(s => s.ItemId == "sub-1"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// A card confirmed for a Trialing subscription never triggers a charge.
    /// </summary>
    /// <remarks>
    /// Saving a card mid-trial and recovering a lapsed one share the same adoption code, and only
    /// one of them is meant to charge anything. Pinned because a mistake here would charge a
    /// trial the moment it added a card, which is exactly what deferred trial charging exists to
    /// prevent.
    /// </remarks>
    [Fact]
    public async Task A_card_confirmed_mid_trial_does_not_trigger_a_charge()
    {
        GivenDueLink(SubscriptionPaymentPurpose.PaymentMethodSetup);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSavedCard();
        GivenSubscription(subscription => subscription.Status = SubscriptionStatus.Trialing);

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _renewals.Verify(
            service => service.RecoverAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A card saved against a subscription that is already running is still adopted.
    /// </summary>
    /// <remarks>
    /// The gap this closes. Adoption sat after an early return taken whenever the subscription was
    /// not Incomplete, so a card added during a trial @D@ the whole point of collecting one mid-trial
    /// @D@ was confirmed by the provider, settled here, and never attached to the billing account.
    /// The subscriber saw a successful Stripe session and still had nothing on file, then failed at
    /// the trial's end for want of a payment method: a silent failure a whole trial away from its
    /// cause.
    /// </remarks>
    [Theory]
    [InlineData(SubscriptionStatus.Trialing)]
    [InlineData(SubscriptionStatus.Active)]
    public async Task A_card_saved_against_a_running_subscription_is_adopted(
        SubscriptionStatus status)
    {
        GivenDueLink(SubscriptionPaymentPurpose.PaymentMethodSetup);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSavedCard();
        GivenSubscription(subscription => subscription.Status = status);

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        _accounts.Verify(
            repository => repository.TrySetProviderCustomerAsync(
                TenantId,
                "acct-1",
                "cus_123",
                "method-1",
                MerchantOrganizationId,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the card the subscriber just entered is the one the next charge has to find");

        _transition.Should().BeNull(
            "the subscription was already running @D@ a card was added to it, not an activation");
    }

    /// <summary>
    /// A card that cannot be adopted leaves the link pending rather than settling it away.
    /// </summary>
    /// <remarks>
    /// Same rule the activation path follows. Settling would end the only record that the card
    /// still needs attaching, and the subscriber has no way to know anything is wrong.
    /// </remarks>
    [Fact]
    public async Task A_card_that_cannot_be_adopted_mid_trial_is_retried_rather_than_settled()
    {
        GivenDueLink(SubscriptionPaymentPurpose.PaymentMethodSetup);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSubscription(subscription => subscription.Status = SubscriptionStatus.Trialing);

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(0);
        _links.Verify(repository => repository.TrySettleAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SubscriptionPaymentLinkState>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A charge confirmed against an already-running subscription still settles and adopts
    /// nothing, because a charge is not a card being added.
    /// </summary>
    [Fact]
    public async Task A_charge_against_a_running_subscription_settles_without_adopting()
    {
        GivenDueLink(SubscriptionPaymentPurpose.InitialCharge);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSavedCard();
        GivenSubscription(subscription => subscription.Status = SubscriptionStatus.Active);

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        _accounts.Verify(
            repository => repository.TrySetProviderCustomerAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A card setup settles into the same confirmed status a charge does, because that status is
    /// how the provider says a thing it was asked to do happened. What must not follow is the
    /// subscription reporting a zero-value record as the charge that opened it.
    /// </summary>
    [Fact]
    public async Task A_stored_card_activates_without_being_recorded_as_the_opening_charge()
    {
        GivenDueLink(SubscriptionPaymentPurpose.PaymentMethodSetup);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSavedCard();

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Active);
        _transition.InitialPaymentDetailId.Should().BeNull(
            "no money moved, so there is no opening charge and no invoice behind one");
    }

    /// <summary>
    /// The D-01 scenario: a card setup that lands directly on Active, not Trialing, is an opening
    /// period that owed nothing today — a discount to zero, or a price already at zero — not a
    /// trial. It still owes a document, the same reasoning a trial invoice already gets, so this
    /// is announced instead of being silently skipped the way an ordinary charge is for a setup.
    /// </summary>
    [Fact]
    public async Task A_card_setup_that_activates_directly_announces_the_opening_period()
    {
        GivenDueLink(SubscriptionPaymentPurpose.PaymentMethodSetup);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSavedCard();
        GivenSubscription(subscription => subscription.InitialChargeAmountMinor = 0);

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        _documents.Verify(
            documents => documents.AnnounceOpeningDiscountAsync(
                It.Is<SubscriptionDetail>(s => s.ItemId == "sub-1"),
                "pay-1",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<FinancialDocumentPerson?>()),
            Times.Once);

        // Never a charge announcement for a card setup — that would issue a document describing
        // money that was never taken.
        _documents.Verify(
            documents => documents.AnnounceChargeAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionChargeKind>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<FinancialDocumentPerson?>()),
            Times.Never);
    }

    /// <summary>
    /// The control: a card setup that starts a trial is announced as a trial, not as an opening
    /// discount — the two document types describe different things, and a trial already has its
    /// own announcement.
    /// </summary>
    [Fact]
    public async Task A_card_setup_that_starts_a_trial_does_not_announce_an_opening_discount()
    {
        GivenDueLink(SubscriptionPaymentPurpose.PaymentMethodSetup);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);
        GivenSavedCard();
        GivenSubscription(subscription => subscription.Trial = new TrialTerms
        {
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddDays(14)
        });

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _documents.Verify(
            documents => documents.AnnounceOpeningDiscountAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<FinancialDocumentPerson?>()),
            Times.Never);
        _documents.Verify(
            documents => documents.AnnounceTrialAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<FinancialDocumentPerson?>()),
            Times.Once);
    }

    [Fact]
    public async Task A_confirmed_setup_waits_until_its_card_is_usable_for_renewal()
    {
        GivenDueLink(SubscriptionPaymentPurpose.PaymentMethodSetup);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);

        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(0);
        _transition.Should().BeNull(
            "provider confirmation without a durable stored method must not grant access");
        _links.Verify(repository => repository.TrySettleAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SubscriptionPaymentLinkState>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A declined charge ends the subscription — the money was refused. A card form that expired
    /// refused nothing, and making somebody cancel and start over for it would be a penalty for
    /// leaving a tab open.
    /// </summary>
    [Fact]
    public async Task A_failed_card_setup_leaves_the_subscription_open_for_another_attempt()
    {
        GivenDueLink(SubscriptionPaymentPurpose.PaymentMethodSetup);
        GivenPayment(PaymentStatuses.Refused, webhookConfirmed: true);

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition.Should().BeNull(
            "the subscription stays Incomplete; only the attempt is over");
        _links.Verify(
            repository => repository.TrySettleAsync(
                TenantId,
                "link-1",
                SubscriptionPaymentLinkState.Abandoned,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the sweep has nothing left to wait for on this one");
    }

    /// <summary>The control: a declined charge still ends it.</summary>
    [Fact]
    public async Task A_declined_opening_charge_still_expires_the_subscription()
    {
        GivenDueLink();
        GivenPayment(PaymentStatuses.Refused, webhookConfirmed: true);

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.IncompleteExpired);
    }

    private readonly Mock<ISubscriptionRenewalService> _renewals = new();

    private readonly Mock<ISubscriptionAuditTrail> _audit = new();

    private SubscriptionActivationProcessor Processor() => new(
        _links.Object,
        _subscriptions.Object,
        _accounts.Object,
        new SubscriptionOutboxEventFactory(),
        _payments.Object,
        _storedMethods.Object,
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<SubscriptionActivationProcessor>.Instance,
        _time,
        audit: _audit.Object,
        renewals: _renewals.Object,
        documents: _documents.Object);

    /// <summary>
    /// The reported bug: a paid signup sat Incomplete for 128 seconds.
    /// </summary>
    /// <remarks>
    /// The first settlement pass ran before Stripe had decided, so it deferred the link 30 seconds
    /// out. The webhook then arrived inside that window — the worker held the confirmation and the
    /// subscription in the same tick — but the sweep only ever asks which links are <em>due</em>,
    /// and this one was not for another 20 seconds. Nothing re-fires at the 30-second mark, so the
    /// activation fell through to the two-minute repair sweep.
    /// </remarks>
    [Fact]
    public async Task A_confirmed_payment_settles_its_link_even_before_its_next_check_is_due()
    {
        GivenLinkForPayment(nextCheckAtUtc: _time.GetUtcNow().UtcDateTime.AddSeconds(20));
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);

        var settled = await Processor().SettleForPaymentsAsync(
            TenantId,
            ["pay-1"],
            CancellationToken.None);

        settled.Should().Be(1);
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Active);
    }

    /// <summary>
    /// The rule that keeps the fast path from making things slower: retry accounting belongs to
    /// the sweep alone.
    /// </summary>
    /// <remarks>
    /// This runs on every webhook tick, and a great many of those carry a payment that is still in
    /// flight. Deferring here would burn attempts against <c>ActivationMaxAttempts</c>, push the
    /// sweep's next look further out, and fill the audit trail with deferral pairs.
    /// </remarks>
    [Fact]
    public async Task An_undecided_payment_leaves_its_link_completely_untouched()
    {
        GivenLinkForPayment(attemptCount: 1);
        GivenPayment(PaymentStatuses.Processing, webhookConfirmed: false);

        var settled = await Processor().SettleForPaymentsAsync(
            TenantId,
            ["pay-1"],
            CancellationToken.None);

        settled.Should().Be(0);
        _links.Verify(
            repository => repository.RescheduleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "deferring here would spend an attempt the sweep is counting, and move its next " +
            "look further away");
        _links.Verify(
            repository => repository.TrySettleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionPaymentLinkState>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _audit.Verify(
            trail => trail.RecordAsync(
                It.IsAny<SubscriptionAuditEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "a pass that decided nothing has nothing to record, and this one runs on every tick");
    }

    [Fact]
    public async Task A_terminally_refused_payment_is_abandoned_through_the_targeted_path()
    {
        GivenLinkForPayment();
        GivenPayment(PaymentStatuses.Refused, webhookConfirmed: true);

        var settled = await Processor().SettleForPaymentsAsync(
            TenantId,
            ["pay-1"],
            CancellationToken.None);

        settled.Should().Be(1);
        _transition!.NewStatus.Should().Be(SubscriptionStatus.IncompleteExpired);
    }

    [Fact]
    public async Task A_payment_no_subscription_is_waiting_on_is_a_no_op()
    {
        // FindByPaymentAsync unmocked: most payments are not a subscription's.
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);

        var settled = await Processor().SettleForPaymentsAsync(
            TenantId,
            ["pay-1"],
            CancellationToken.None);

        settled.Should().Be(0);
        _payments.Verify(
            repository => repository.GetByIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "there is no link to settle, so there is no reason to read the payment");
    }

    [Fact]
    public async Task A_link_whose_outcome_was_already_applied_is_a_no_op()
    {
        GivenLinkForPayment(state: SubscriptionPaymentLinkState.Applied);
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);

        var settled = await Processor().SettleForPaymentsAsync(
            TenantId,
            ["pay-1"],
            CancellationToken.None);

        settled.Should().Be(0);
        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "this outcome has already been carried across");
    }

    /// <summary>
    /// The targeted pass now races the sweep, the durable queue and other replicas.
    /// </summary>
    /// <remarks>
    /// The transition is a compare-and-set, so exactly one racer wins it. A loser must leave the
    /// link pending: settling it would erase the record the winner still needs to settle itself.
    /// </remarks>
    [Fact]
    public async Task Losing_the_transition_race_through_the_targeted_path_leaves_the_link_unsettled()
    {
        GivenLinkForPayment();
        GivenPayment(PaymentStatuses.Authorized, webhookConfirmed: true);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var settled = await Processor().SettleForPaymentsAsync(
            TenantId,
            ["pay-1"],
            CancellationToken.None);

        settled.Should().Be(0);
        _links.Verify(
            repository => repository.TrySettleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionPaymentLinkState>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "the winner of the transition settles the link; a loser that settled it first would " +
            "take that record away");
    }

    /// <summary>The link the targeted pass finds by payment id, rather than by being due.</summary>
    private void GivenLinkForPayment(
        SubscriptionPaymentLinkState state = SubscriptionPaymentLinkState.Pending,
        DateTime? nextCheckAtUtc = null,
        int attemptCount = 0,
        SubscriptionPaymentPurpose purpose = SubscriptionPaymentPurpose.InitialCharge) =>
        _links
            .Setup(repository => repository.FindByPaymentAsync(
                TenantId,
                "pay-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionPaymentLink
            {
                ItemId = "link-1",
                TenantId = TenantId,
                OrganizationId = "org-1",
                SubscriptionId = "sub-1",
                PaymentDetailId = "pay-1",
                Purpose = purpose,
                CorrelationId = "corr-1",
                State = state,
                AttemptCount = attemptCount,
                NextCheckAtUtc = nextCheckAtUtc ?? _time.GetUtcNow().UtcDateTime
            });

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
