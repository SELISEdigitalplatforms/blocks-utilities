using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Reserving a campaign at signup: the ordering that makes a crash recoverable, and the retry
/// that has to land on the same reservation rather than a second one.
/// </summary>
/// <remarks>
/// <see cref="CampaignRedemptionRepository"/>'s own concurrency guarantees are proven against a
/// real MongoDB in <c>CampaignRedemptionConcurrencyTests</c>, not here. This suite is entirely
/// about a different question a mock answers perfectly well: given a particular reservation
/// outcome, does this service make the right call afterwards -- does it persist the subscription
/// at all, does it undo the one it already persisted, does it recognise its own earlier attempt.
/// </remarks>
public sealed class SubscriptionCreationServiceCampaignTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<ICampaignRedemptionRepository> _redemptions = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<ISubscriptionBillingProfileGuard> _billingProfile = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail? _created;
    private CampaignRedemption? _reservationAttempted;

    public SubscriptionCreationServiceCampaignTests()
    {
        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _billingProfile
            .Setup(guard => guard.ContactDefaultsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingContactDefaults("Ada Byron", "billing@northwind.example"));

        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "professional", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPlan());
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice());

        _accounts
            .Setup(repository => repository.GetOrCreateAndReconcileAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount account, CancellationToken _) => account);

        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionDetail, CancellationToken>(
                (subscription, _) => _created = subscription)
            .ReturnsAsync(true);
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);
        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "free1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CampaignDiscount());

        _redemptions
            .Setup(repository => repository.TryReserveAsync(
                It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()))
            .Callback<CampaignRedemption, CancellationToken>(
                (reservation, _) => _reservationAttempted = reservation)
            .ReturnsAsync(CampaignReservationOutcome.Reserved);
    }

    [Fact]
    public async Task A_campaign_discount_is_reserved_only_after_the_subscription_is_persisted()
    {
        var request = NewRequest();
        request.DiscountCode = "free1";

        var callOrder = new List<string>();
        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionDetail, CancellationToken>((subscription, _) =>
            {
                callOrder.Add("persist");
                _created = subscription;
            })
            .ReturnsAsync(true);
        _redemptions
            .Setup(repository => repository.TryReserveAsync(
                It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()))
            .Callback<CampaignRedemption, CancellationToken>((reservation, _) =>
            {
                callOrder.Add("reserve");
                _reservationAttempted = reservation;
            })
            .ReturnsAsync(CampaignReservationOutcome.Reserved);

        var result = await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Reserving before persisting would leave an orphaned reservation with no subscription to
        // trace it back to if the process died in between -- exactly the crash window this
        // ordering exists to avoid.
        callOrder.Should().Equal("persist", "reserve");
        _reservationAttempted!.SubscriptionId.Should().Be(_created!.ItemId);
        _reservationAttempted.DiscountId.Should().Be(_created.Discount!.DiscountId);
    }

    [Fact]
    public async Task A_reservation_refused_by_a_different_subscription_expires_the_one_just_created()
    {
        _redemptions
            .Setup(repository => repository.TryReserveAsync(
                It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CampaignReservationOutcome.HeldByAnotherSubscription);

        SubscriptionTransition? transition = null;
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, It.IsAny<string>(), It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, given, _) => transition = given)
            .ReturnsAsync(true);

        var request = NewRequest();
        request.DiscountCode = "free1";

        var result = await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_already_redeemed");

        // Nothing here deletes a subscription record; the only recovery available is to move the
        // one just created straight to a terminal state, which is also what frees this
        // organization's reservation slot for a genuine next attempt.
        transition.Should().NotBeNull();
        transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Incomplete);
        transition.NewStatus.Should().Be(SubscriptionStatus.IncompleteExpired);
    }

    [Fact]
    public async Task A_retry_after_a_crash_between_persisting_and_reserving_completes_the_existing_reservation()
    {
        // The exact shape of the crash window: a prior attempt's subscription is already
        // Incomplete and already carries this same discount's terms, but nothing ever reserved it.
        var stranded = NewSubscription();

        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stranded);
        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // the organization-level unique index refuses a second Incomplete

        var request = NewRequest();
        request.DiscountCode = "free1";

        var result = await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // The reservation is completed against the STRANDED subscription's own id, not a new one
        // -- there is no second subscription to reserve for.
        _reservationAttempted!.SubscriptionId.Should().Be(stranded.ItemId);
        result.Value!.ItemId.Should().Be(stranded.ItemId);
    }

    [Fact]
    public async Task A_retry_for_a_genuinely_different_discount_is_not_treated_as_a_recoverable_crash()
    {
        var strandedForADifferentCode = NewSubscription();
        strandedForADifferentCode.Discount = new DiscountTerms
        {
            Code = "other", DiscountId = "discount-other", Campaign = new CampaignTerms()
        };

        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strandedForADifferentCode);
        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = NewRequest();
        request.DiscountCode = "free1";

        var result = await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_already_active");
        _redemptions.Verify(
            repository => repository.TryReserveAsync(
                It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_standard_discount_never_touches_the_redemption_repository()
    {
        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "launch25", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                Terms = new DiscountTerms
                {
                    Code = "launch25", Kind = DiscountKind.Percent, PercentBasisPoints = 2500
                }
            });

        var request = NewRequest();
        request.DiscountCode = "launch25";

        var result = await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _redemptions.Verify(
            repository => repository.TryReserveAsync(
                It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task With_no_redemption_repository_wired_a_campaign_discount_is_refused_rather_than_granted()
    {
        // Fail closed: the same choice the constructor's own doc comment makes. Nothing here
        // silently grants a one-use campaign with no mechanism actually enforcing one-use.
        var service = new SubscriptionCreationService(
            _catalogue.Object,
            _subscriptions.Object,
            _discounts.Object,
            _accounts.Object,
            new CreateSubscriptionRequestValidator(),
            NullLogger<SubscriptionCreationService>.Instance,
            _time,
            billingProfile: _billingProfile.Object);

        var request = NewRequest();
        request.DiscountCode = "free1";

        var result = await service.CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_reservation_conflict");
    }

    [Fact]
    public async Task A_campaign_not_yet_started_is_refused_before_ever_reaching_the_ledger()
    {
        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "notyet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                Terms = new DiscountTerms { Code = "notyet", Kind = DiscountKind.Percent, PercentBasisPoints = 10_000 },
                Campaign = new CampaignTerms
                {
                    Kind = CampaignKind.FreeOpeningCalendarPeriod,
                    RedeemableFromUtc = _time.GetUtcNow().UtcDateTime.AddDays(1),
                    RedeemableUntilUtc = _time.GetUtcNow().UtcDateTime.AddDays(30)
                }
            });

        var request = NewRequest();
        request.DiscountCode = "notyet";

        var result = await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_not_started");
        _redemptions.Verify(
            repository => repository.TryReserveAsync(
                It.IsAny<CampaignRedemption>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_campaign_past_its_window_is_refused_as_expired()
    {
        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, "over", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                Terms = new DiscountTerms { Code = "over", Kind = DiscountKind.Percent, PercentBasisPoints = 10_000 },
                Campaign = new CampaignTerms
                {
                    Kind = CampaignKind.FreeOpeningCalendarPeriod,
                    RedeemableFromUtc = _time.GetUtcNow().UtcDateTime.AddDays(-30),
                    RedeemableUntilUtc = _time.GetUtcNow().UtcDateTime.AddDays(-1)
                }
            });

        var request = NewRequest();
        request.DiscountCode = "over";

        var result = await Service().CreateAsync(request, Context(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_expired");
    }

    private SubscriptionCreationService Service() => new(
        _catalogue.Object,
        _subscriptions.Object,
        _discounts.Object,
        _accounts.Object,
        new CreateSubscriptionRequestValidator(),
        NullLogger<SubscriptionCreationService>.Instance,
        _time,
        billingProfile: _billingProfile.Object,
        redemptions: _redemptions.Object);

    private static SubscriptionContext Context() =>
        new(TenantId, OrganizationId, "actor-1", "user-1");

    private static CreateSubscriptionRequest NewRequest() => new()
    {
        PlanCode = "professional",
        PriceId = "price-1",
        TimeZoneId = "Europe/Zurich",
        Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = 12 }]
    };

    private static Discount CampaignDiscount() => new()
    {
        ItemId = "discount-free1",
        Version = 1,
        Terms = new DiscountTerms { Code = "free1", Kind = DiscountKind.Percent, PercentBasisPoints = 10_000 },
        Campaign = new CampaignTerms
        {
            Kind = CampaignKind.FreeOpeningCalendarPeriod,
            OneUsePerOrganization = true,
            RedeemableFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RedeemableUntilUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }
    };

    private static CampaignTerms CampaignTerms() => new()
    {
        Kind = CampaignKind.FreeOpeningCalendarPeriod,
        OneUsePerOrganization = true
    };

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "subscription-stranded",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Incomplete,
        Discount = new DiscountTerms
        {
            Code = "free1", DiscountId = "discount-free1", DiscountVersion = 1,
            Campaign = CampaignTerms()
        }
    };

    private static Plan NewPlan() => new()
    {
        ItemId = "plan-1",
        TenantId = TenantId,
        Code = "professional",
        DisplayName = "Professional",
        Status = CatalogueStatus.Active,
        Version = 3,
        QuantityItems =
        [
            new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat", DefaultQuantity = 1 }
        ]
    };

    private static Price NewPrice() => new()
    {
        ItemId = "price-1",
        TenantId = TenantId,
        PlanId = "plan-1",
        CurrencyCode = "CHF",
        UnitAmountMinor = 8900,
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        QuantityItemKey = "seat",
        Status = CatalogueStatus.Active
    };
}
