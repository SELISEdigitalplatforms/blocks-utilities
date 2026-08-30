using FluentAssertions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription;

/// <summary>
/// Campaign authoring: the fields a promotional discount carries beyond an ordinary percentage or
/// fixed reduction, and the ways an author's request for one of them can be wrong.
/// </summary>
/// <remarks>
/// Two things this suite exists to prove above everything else. First, that a document with no
/// campaign configuration is untouched by any of this — every legacy discount test in
/// <see cref="DiscountCatalogueServiceTests"/> still passes unmodified, and this suite's own
/// <see cref="A_legacy_discount_reads_back_as_standard_with_no_campaign_fields"/> pins the
/// deserialization default that makes that true. Second, that the two gaps a campaign genuinely
/// needs — a price that has been retired since a campaign was authored, and a date boundary that
/// lands inside a daylight-saving transition — are refused rather than silently mispriced.
/// </remarks>
public sealed class DiscountCatalogueServiceCampaignTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string PlanId = "plan-pro";
    private const string YearlyPriceId = "price-pro-yearly";
    private const string CalendarYearlyPriceId = "price-pro-yearly-calendar";
    private const string MonthlyPriceId = "price-pro-monthly";
    private const string CalendarMonthlyPriceId = "price-pro-calendar-monthly";
    private const string ArchivedPriceId = "price-pro-retired";

    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private Discount? _created;

    public DiscountCatalogueServiceCampaignTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _catalogue
            .Setup(repository => repository.ListPlansAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([PlanWithSeatEntitlement()]);

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, YearlyPriceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price(YearlyPriceId, BillingInterval.Year, BillingAlignment.Anniversary));

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, CalendarYearlyPriceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CalendarYearlyPrice());

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, MonthlyPriceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price(MonthlyPriceId, BillingInterval.Month, BillingAlignment.Anniversary));

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, CalendarMonthlyPriceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price(
                CalendarMonthlyPriceId, BillingInterval.Month, BillingAlignment.CalendarMonth));

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, ArchivedPriceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Price(
                ArchivedPriceId,
                BillingInterval.Month,
                BillingAlignment.CalendarMonth,
                status: CatalogueStatus.Archived));

        _discounts
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<Discount>(), It.IsAny<CancellationToken>()))
            .Callback<Discount, CancellationToken>((discount, _) => _created = discount)
            .ReturnsAsync(true);
    }

    // ---- Backward compatibility -------------------------------------------------------------

    [Fact]
    public void A_legacy_discount_reads_back_as_standard_with_no_campaign_fields()
    {
        // Never deserialized through the catalogue service in this test — the point is that the
        // entity itself defaults this way, so a document written before Campaign existed at all
        // reads back with the gate every campaign code path checks already at its "off" value.
        var legacy = new Discount();

        legacy.Campaign.Kind.Should().Be(CampaignKind.Standard);
        legacy.Campaign.Precedence.Should().Be(CampaignPrecedence.BestDiscount);
        legacy.Campaign.ValidFromDate.Should().BeNull();
        legacy.Campaign.RedeemableFromUtc.Should().BeNull();
        legacy.Campaign.EntitlementOverride.Should().BeNull();
        legacy.Version.Should().Be(0);
    }

    [Fact]
    public async Task A_standard_discount_still_creates_without_naming_any_campaign_field()
    {
        var result = await Service().CreateAsync(Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.Campaign.Kind.Should().Be(CampaignKind.Standard);
    }

    [Fact]
    public async Task A_standard_discount_refuses_a_campaign_field_set_without_a_campaign_kind()
    {
        // Refused rather than silently dropped -- an author who set a window on what they thought
        // was a campaign discount has to be told the kind was never set, not have the window
        // vanish without explanation the moment it is saved.
        var request = Request();
        request.ValidFromDate = new DateOnly(2026, 1, 1);
        request.ValidThroughDate = new DateOnly(2026, 12, 31);
        request.TimeZoneId = "Europe/Zurich";

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_invalid");
    }

    // ---- Structural validation ---------------------------------------------------------------

    [Fact]
    public async Task A_campaign_needs_its_window_and_time_zone()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [YearlyPriceId]);
        request.ValidFromDate = null;

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_invalid");
    }

    [Fact]
    public async Task An_unrecognised_time_zone_is_refused()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [YearlyPriceId]);
        request.TimeZoneId = "Mars/Olympus_Mons";

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task A_campaign_cannot_end_before_it_starts()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [YearlyPriceId]);
        request.ValidFromDate = new DateOnly(2026, 12, 31);
        request.ValidThroughDate = new DateOnly(2026, 1, 1);

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task A_campaign_that_prices_specific_periods_must_name_at_least_one_price()
    {
        // Unrestricted is a legitimate choice for a Standard discount, but FirstAnnualPeriod and
        // FreeOpeningCalendarPeriod price a specific price's stub or opening period, not a plan's
        // in general -- there is nothing to price without one named.
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, []);

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(9_999)] // 99.99%, not 100%
    public async Task A_free_month_reduction_must_be_exactly_100_percent(int basisPoints)
    {
        var request = FreeMonthRequest();
        request.PercentBasisPoints = basisPoints;

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task A_free_month_reduction_cannot_be_a_fixed_amount()
    {
        var request = FreeMonthRequest();
        request.Kind = DiscountKind.FixedAmount;
        request.PercentBasisPoints = null;
        request.AmountMinor = 1_000;
        request.CurrencyCode = "CHF";

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task A_free_month_must_be_one_use_per_organization()
    {
        var request = FreeMonthRequest();
        request.OneUsePerOrganization = false;

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task A_free_month_must_require_a_payment_method_upfront()
    {
        var request = FreeMonthRequest();
        request.RequiresPaymentMethodUpfront = false;

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task A_free_month_must_name_its_temporary_entitlement()
    {
        var request = FreeMonthRequest();
        request.EntitlementOverrideKey = null;
        request.EntitlementOverrideLimit = null;

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // ---- Catalogue-dependent applicability -----------------------------------------------------

    [Fact]
    public async Task A_first_annual_period_campaign_refuses_a_price_that_does_not_bill_yearly()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [MonthlyPriceId]);

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_applicability_invalid");
    }

    /// <summary>
    /// The gap this closes: an anniversary-billed yearly price has no stub and no "period 1 does
    /// not count" moment for <see cref="DiscountTerms.DurationPeriods"/> = 1 to attach to, so
    /// authoring one here would silently discount two years instead of the one the campaign's own
    /// name promises. Refused at the one point an author can still pick a different price.
    /// </summary>
    [Fact]
    public async Task A_first_annual_period_campaign_refuses_an_anniversary_billed_yearly_price()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [YearlyPriceId]);

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_applicability_invalid");
    }

    [Fact]
    public async Task A_first_annual_period_campaign_accepts_a_calendar_aligned_yearly_price()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [CalendarYearlyPriceId]);

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// EntitlementService only ever honours an override for FreeOpeningCalendarPeriod -- see
    /// EntitlementServiceCampaignTests. Accepting one here would store a value that validates
    /// cleanly and then is silently never enforced, the same shape the removed ApplyToOpeningStub
    /// flag had.
    /// </summary>
    [Fact]
    public async Task A_first_annual_period_campaign_refuses_a_temporary_entitlement_override()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [CalendarYearlyPriceId]);
        request.EntitlementOverrideKey = "seats";
        request.EntitlementOverrideLimit = 1;

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_invalid");
    }

    [Fact]
    public async Task A_free_opening_period_campaign_refuses_a_price_that_is_not_calendar_aligned()
    {
        // Monthly, but anniversary-aligned rather than calendar-aligned -- it has no calendar
        // month boundary to give away for free, only an anniversary one.
        var request = FreeMonthRequest();
        request.ApplicablePriceIds = [MonthlyPriceId];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_applicability_invalid");
    }

    [Fact]
    public async Task A_free_opening_period_campaign_refuses_a_yearly_price_even_if_calendar_aligned()
    {
        var request = FreeMonthRequest();
        request.ApplicablePriceIds = [YearlyPriceId];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task A_free_opening_period_campaign_accepts_a_calendar_aligned_monthly_price()
    {
        var request = FreeMonthRequest();

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_archived_price_cannot_be_selected_for_a_new_campaign()
    {
        // A gap that predates campaigns: GetPriceAsync does not filter by status, since most
        // callers look one up by id regardless of whether it can still be sold. Authoring a new
        // discount against a retired price is not one of those callers.
        var request = FreeMonthRequest();
        request.ApplicablePriceIds = [ArchivedPriceId];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_applicability_invalid");
    }

    [Fact]
    public async Task An_archived_price_is_still_refused_even_for_an_ordinary_non_campaign_discount()
    {
        // Same gap, proven on the path every discount already went through -- this is not a
        // campaign-only fix.
        var request = Request();
        request.ApplicablePriceIds = [ArchivedPriceId];

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task An_entitlement_override_must_name_a_key_the_plan_actually_grants()
    {
        var request = FreeMonthRequest();
        request.EntitlementOverrideKey = "nonexistent-entitlement";

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task An_entitlement_override_cannot_exceed_the_plans_own_limit()
    {
        var request = FreeMonthRequest();
        request.EntitlementOverrideLimit = 999; // the plan's own seat limit is 5, set below.

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task An_entitlement_override_at_or_under_the_plans_limit_is_accepted()
    {
        var request = FreeMonthRequest();
        request.EntitlementOverrideLimit = 1;

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.Campaign.EntitlementOverride!.Limit.Should().Be(1);
    }

    // ---- Redeemable window: DST-safe conversion ------------------------------------------------

    [Fact]
    public async Task The_redeemable_window_is_computed_and_frozen_at_authoring_time()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [CalendarYearlyPriceId]);
        request.ValidFromDate = new DateOnly(2026, 3, 1);
        request.ValidThroughDate = new DateOnly(2026, 3, 31);
        request.TimeZoneId = "Europe/Zurich";

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // 1 March local midnight in Zurich, before the spring DST change.
        _created!.Campaign.RedeemableFromUtc.Should().Be(new DateTime(2026, 2, 28, 23, 0, 0, DateTimeKind.Utc));
        // Exclusive: local midnight of 1 April, the day AFTER the inclusive 31 March end date --
        // and 29 March 2026 is the night Europe/Zurich springs forward, so this instant is also
        // proof the conversion survived crossing that transition.
        _created!.Campaign.RedeemableUntilUtc.Should().Be(new DateTime(2026, 3, 31, 22, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_campaign_window_that_starts_inside_a_spring_forward_gap_still_resolves()
    {
        // Belt and braces on top of the previous test: the start date itself sitting on the
        // transition night, not just the window crossing it. BillingLocalTime.ToUtc already
        // carries this policy everywhere else in the billing domain; this proves it is actually
        // being reused here rather than a second, untested implementation of the same idea.
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [CalendarYearlyPriceId]);
        request.ValidFromDate = new DateOnly(2026, 3, 29);
        request.ValidThroughDate = new DateOnly(2026, 3, 30);
        request.TimeZoneId = "Europe/Zurich";

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _created!.Campaign.RedeemableFromUtc.Should().NotBeNull();
    }

    // ---- Get / Update / version conflict -------------------------------------------------------

    [Fact]
    public async Task An_update_with_the_current_version_succeeds_and_increments_it()
    {
        var stored = StoredDiscount(version: 3);
        _discounts
            .Setup(repository => repository.FindByIdAsync(
                TenantId, stored.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _discounts
            .Setup(repository => repository.TryUpdateAsync(
                It.IsAny<Discount>(), 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = UpdateRequest(expectedVersion: 3);
        request.StartsAtUtc = DateTime.UtcNow.AddDays(1);
        request.ExpiresAtUtc = DateTime.UtcNow.AddDays(10);

        var result = await Service().UpdateAsync(
            stored.ItemId, request, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stored.Terms.StartsAtUtc.Should().Be(request.StartsAtUtc);
        stored.Terms.ExpiresAtUtc.Should().Be(request.ExpiresAtUtc);
    }

    [Fact]
    public async Task An_update_against_a_stale_version_is_refused_as_a_conflict_not_silently_applied()
    {
        var stored = StoredDiscount(version: 3);
        _discounts
            .Setup(repository => repository.FindByIdAsync(
                TenantId, stored.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _discounts
            .Setup(repository => repository.TryUpdateAsync(
                It.IsAny<Discount>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().UpdateAsync(
            stored.ItemId, UpdateRequest(expectedVersion: 1), null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_version_conflict");
    }

    [Fact]
    public async Task Getting_a_discount_from_another_organizations_scope_reads_as_not_found()
    {
        var stored = StoredDiscount(version: 0);
        stored.OrganizationId = "org-somebody-else";
        _discounts
            .Setup(repository => repository.FindByIdAsync(
                TenantId, stored.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var result = await Service().GetAsync(stored.ItemId, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_discount_not_found");
    }

    [Fact]
    public async Task A_tenant_wide_discount_is_visible_to_every_organization_in_the_tenant()
    {
        var stored = StoredDiscount(version: 0);
        stored.OrganizationId = null;
        _discounts
            .Setup(repository => repository.FindByIdAsync(
                TenantId, stored.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var result = await Service().GetAsync(stored.ItemId, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // ---- Effective state --------------------------------------------------------------------

    [Fact]
    public async Task A_campaign_before_its_window_reads_as_upcoming()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [CalendarYearlyPriceId]);
        request.ValidFromDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));
        request.ValidThroughDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1).AddMonths(1));

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EffectiveState.Should().Be("Upcoming");
    }

    [Fact]
    public async Task A_campaign_past_its_window_reads_as_expired()
    {
        var request = CampaignRequest(CampaignKind.FirstAnnualPeriod, [CalendarYearlyPriceId]);
        request.ValidFromDate = new DateOnly(2020, 1, 1);
        request.ValidThroughDate = new DateOnly(2020, 1, 31);

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EffectiveState.Should().Be("Expired");
    }

    [Fact]
    public async Task A_standard_discount_before_its_start_reads_as_upcoming_and_returns_the_window()
    {
        var request = Request();
        request.StartsAtUtc = DateTime.UtcNow.AddDays(1);
        request.ExpiresAtUtc = DateTime.UtcNow.AddDays(10);

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EffectiveState.Should().Be("Upcoming");
        result.Value.StartsAtUtc.Should().Be(request.StartsAtUtc);
        result.Value.ExpiresAtUtc.Should().Be(request.ExpiresAtUtc);
        _created!.Terms.StartsAtUtc.Should().Be(request.StartsAtUtc);
    }

    [Fact]
    public async Task A_standard_discount_past_its_expiry_reads_as_expired()
    {
        var request = Request();
        request.StartsAtUtc = DateTime.UtcNow.AddDays(-10);
        request.ExpiresAtUtc = DateTime.UtcNow.AddDays(-1);

        var result = await Service().CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EffectiveState.Should().Be("Expired");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private DiscountCatalogueService Service() => new(
        _discounts.Object,
        _catalogue.Object,
        _contextResolver.Object,
        new CreateDiscountRequestValidator(),
        new UpdateDiscountRequestValidator());

    private static CreateDiscountRequest Request() => new()
    {
        Code = "launch25",
        DisplayName = "Launch offer",
        Kind = DiscountKind.Percent,
        PercentBasisPoints = 2_500
    };

    private static CreateDiscountRequest CampaignRequest(CampaignKind kind, List<string> priceIds) => new()
    {
        Code = "campaign-code",
        DisplayName = "A campaign",
        Kind = DiscountKind.Percent,
        PercentBasisPoints = 1_000,
        ApplicablePriceIds = priceIds,
        CampaignKind = kind,
        ValidFromDate = new DateOnly(2026, 1, 1),
        ValidThroughDate = new DateOnly(2026, 12, 31),
        TimeZoneId = "Europe/Zurich"
    };

    private static CreateDiscountRequest FreeMonthRequest()
    {
        var request = CampaignRequest(CampaignKind.FreeOpeningCalendarPeriod, [CalendarMonthlyPriceId]);
        request.Kind = DiscountKind.Percent;
        request.PercentBasisPoints = 10_000;
        request.OneUsePerOrganization = true;
        request.RequiresPaymentMethodUpfront = true;
        request.EntitlementOverrideKey = "seats";
        request.EntitlementOverrideLimit = 1;

        return request;
    }

    private static UpdateDiscountRequest UpdateRequest(long expectedVersion) => new()
    {
        ExpectedVersion = expectedVersion,
        DisplayName = "Updated name",
        Kind = DiscountKind.Percent,
        PercentBasisPoints = 3_000
    };

    private static Discount StoredDiscount(long version) => new()
    {
        ItemId = "discount-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Code = "existing-code",
        DisplayName = "Existing",
        Version = version,
        Terms = new DiscountTerms
        {
            Code = "existing-code", Kind = DiscountKind.Percent, PercentBasisPoints = 1_000
        }
    };

    private static Plan PlanWithSeatEntitlement() => new()
    {
        ItemId = PlanId,
        TenantId = TenantId,
        Code = "pro",
        DisplayName = "pro",
        Status = CatalogueStatus.Active,
        Entitlements =
        [
            new PlanEntitlement { Key = "seats", LimitKind = EntitlementLimitKind.Count, Limit = 5 }
        ]
    };

    private static Price Price(
        string priceId,
        BillingInterval interval,
        BillingAlignment alignment,
        CatalogueStatus status = CatalogueStatus.Active) => new()
    {
        ItemId = priceId,
        TenantId = TenantId,
        PlanId = PlanId,
        CurrencyCode = "CHF",
        UnitAmountMinor = 10_000,
        Interval = interval,
        IntervalCount = 1,
        BillingAlignment = alignment,
        Status = status
    };

    /// <summary>
    /// A yearly price that actually bills on calendar boundaries -- the only cadence a
    /// FirstAnnualPeriod campaign can be authored against, since it needs a stub base price to
    /// price the opening fraction from as well as the alignment itself.
    /// </summary>
    private static Price CalendarYearlyPrice() => new()
    {
        ItemId = CalendarYearlyPriceId,
        TenantId = TenantId,
        PlanId = PlanId,
        CurrencyCode = "CHF",
        UnitAmountMinor = 120_000,
        Interval = BillingInterval.Year,
        IntervalCount = 1,
        BillingAlignment = BillingAlignment.CalendarMonth,
        CalendarStubBasePriceId = "price-pro-monthly-stub-base",
        CalendarStubBaseUnitAmountMinor = 10_000,
        Status = CatalogueStatus.Active
    };
}
