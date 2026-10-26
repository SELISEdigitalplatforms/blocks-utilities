using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription.Credits;

/// <summary>
/// Authoring purchasable credits: the per-meter terms on a plan, and discount codes scoped to a
/// credit purchase (spec 002, phase A).
/// </summary>
/// <remarks>
/// Guards against the authoring mistakes that only surface at the till: an offer with no ceiling on
/// a single card charge, a credit price the subscriber is shown in a currency they cannot pay in,
/// and a code meant for a one-off top-up quietly discounting every month of a subscription — or
/// the other way round.
/// </remarks>
public sealed class CreditTermsAuthoringTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    // ---- plan meter terms ------------------------------------------------------------------

    [Fact]
    public async Task Enabled_credits_with_a_price_and_bounds_are_accepted()
    {
        var result = await new PlanDefinitionRequestValidator().ValidateAsync(PlanWith(TopUp()));

        result.IsValid.Should().BeTrue(
            "a priced, bounded offer is the ordinary case and must save: {0}",
            string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));
    }

    [Fact]
    public async Task Enabled_credits_without_a_maximum_purchase_are_refused()
    {
        var topUp = TopUp();
        topUp.MaxQuantity = 0;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(PlanWith(topUp));

        result.Errors.Should().Contain(
            error => error.ErrorCode == "subscription_meter_top_up_invalid",
            "the maximum is what bounds one charge to a stored card (AC-28)");
    }

    [Fact]
    public async Task A_fractional_purchase_bound_is_refused()
    {
        var topUp = TopUp();
        topUp.MinQuantity = 1.5m;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(PlanWith(topUp));

        result.IsValid.Should().BeFalse("credits are bought in whole units, whatever the meter's scale");
    }

    [Fact]
    public async Task Two_credit_prices_in_one_currency_are_refused()
    {
        var topUp = TopUp();
        topUp.UnitPrices.Add(new MeterTopUpPriceRequest { CurrencyCode = "chf", UnitAmountMinor = 5 });

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(PlanWith(topUp));

        result.IsValid.Should().BeFalse("the quote would depend on which price happened to be read first");
    }

    [Fact]
    public async Task A_taxed_credit_price_must_say_whether_the_tax_is_included()
    {
        var topUp = TopUp();
        topUp.TaxRateBasisPoints = 810;
        topUp.TaxMode = null;

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(PlanWith(topUp));

        result.IsValid.Should().BeFalse("one rate and one price mean two different totals without a mode");
    }

    [Fact]
    public async Task Paused_credits_are_saved_without_being_validated()
    {
        var topUp = new MeterTopUpRequest { Enabled = false };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(PlanWith(topUp));

        result.IsValid.Should().BeTrue("an author pausing sales must not be blocked by a half-written price");
    }

    [Fact]
    public async Task A_fixed_credit_lifetime_is_accepted()
    {
        var topUp = TopUp();
        topUp.ExpiresAfter = new CreditExpiryDurationRequest { Count = 90, Unit = CreditExpiryUnit.Day };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(PlanWith(topUp));

        result.IsValid.Should().BeTrue(
            "credits that last a set time are an ordinary offer: {0}",
            string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));
    }

    [Fact]
    public async Task A_zero_length_credit_lifetime_is_refused()
    {
        var topUp = TopUp();
        topUp.ExpiresAfter = new CreditExpiryDurationRequest { Count = 0, Unit = CreditExpiryUnit.Month };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(PlanWith(topUp));

        result.IsValid.Should().BeFalse("credits that expire the instant they are bought are a trap, not an offer");
    }

    [Fact]
    public async Task An_unknown_credit_lifetime_unit_is_refused()
    {
        var topUp = TopUp();
        topUp.ExpiresAfter = new CreditExpiryDurationRequest { Count = 1, Unit = (CreditExpiryUnit)9 };

        var result = await new PlanDefinitionRequestValidator().ValidateAsync(PlanWith(topUp));

        result.IsValid.Should().BeFalse("a unit the expiry arithmetic cannot read must be refused at authoring, not at purchase");
    }

    [Fact]
    public void A_month_long_lifetime_follows_the_calendar_rather_than_counting_days()
    {
        var lifetime = new CreditExpiryDuration { Count = 1, Unit = CreditExpiryUnit.Month };

        lifetime.EndFrom(new DateTime(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc),
                "a month from the 31st lands on the last day of February, the same rule a billing period follows");
    }

    [Fact]
    public void A_week_long_lifetime_is_seven_days()
    {
        var lifetime = new CreditExpiryDuration { Count = 2, Unit = CreditExpiryUnit.Week };

        lifetime.EndFrom(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_subscriber_is_told_the_fixed_lifetime_of_the_credits_on_offer()
    {
        var subscription = SubscriptionWith(new MeterTopUpTerms
        {
            Enabled = true,
            UnitPrices = [new MeterTopUpPrice { CurrencyCode = "CHF", UnitAmountMinor = 10 }],
            MaxQuantity = 1_000,
            ExpiryPolicy = MeterResetPolicy.Never,
            ExpiresAfter = new CreditExpiryDuration { Count = 6, Unit = CreditExpiryUnit.Month }
        });

        var credit = PricedMapper().ToResponse(subscription).Meters.Single().CreditPurchase;

        credit!.ExpiresAfter.Should().NotBeNull("a buyer has to see how long the credits last before paying");
        credit.ExpiresAfter!.Count.Should().Be(6);
        credit.ExpiresAfter.Unit.Should().Be("Month");
    }

    [Fact]
    public void A_snapshot_of_credit_terms_does_not_share_its_prices_with_the_catalogue()
    {
        var catalogue = new MeterTopUpTerms
        {
            Enabled = true,
            UnitPrices = [new MeterTopUpPrice { CurrencyCode = "CHF", UnitAmountMinor = 10 }],
            MaxQuantity = 1_000,
            ExpiresAfter = new CreditExpiryDuration { Count = 30, Unit = CreditExpiryUnit.Day }
        };

        var snapshot = catalogue.Clone();
        catalogue.UnitPrices[0].UnitAmountMinor = 99;
        catalogue.ExpiresAfter!.Count = 7;

        snapshot.UnitPrices[0].UnitAmountMinor.Should().Be(10,
            "a catalogue price rise must never reprice a subscriber who already holds the plan");
        snapshot.ExpiresAfter!.Count.Should().Be(30,
            "shortening the lifetime in the catalogue must not shorten what a subscriber already bought");
    }

    // ---- what a subscriber is shown -------------------------------------------------------

    [Fact]
    public void A_subscriber_sees_the_credit_price_in_their_own_currency()
    {
        var subscription = SubscriptionWith(new MeterTopUpTerms
        {
            Enabled = true,
            UnitPrices =
            [
                new MeterTopUpPrice { CurrencyCode = "USD", UnitAmountMinor = 12 },
                new MeterTopUpPrice { CurrencyCode = "CHF", UnitAmountMinor = 10 }
            ],
            MinQuantity = 10,
            MaxQuantity = 1_000,
            ExpiryPolicy = MeterResetPolicy.Never
        });

        var credit = PricedMapper().ToResponse(subscription).Meters.Single().CreditPurchase;

        credit.Should().NotBeNull();
        credit!.CurrencyCode.Should().Be("CHF", "a purchase is only ever charged in the subscription's currency");
        credit.UnitAmountMinor.Should().Be(10);
        credit.UnitAmount.Should().Be("0.10");
        credit.ExpiryPolicy.Should().Be("Never");
    }

    [Fact]
    public void Credits_priced_only_in_another_currency_are_not_offered()
    {
        var subscription = SubscriptionWith(new MeterTopUpTerms
        {
            Enabled = true,
            UnitPrices = [new MeterTopUpPrice { CurrencyCode = "USD", UnitAmountMinor = 12 }],
            MaxQuantity = 1_000
        });

        PricedMapper().ToResponse(subscription).Meters.Single().CreditPurchase.Should().BeNull(
            "showing a price the subscriber cannot pay in invites a purchase that will be refused");
    }

    [Fact]
    public void Paused_credits_are_not_offered()
    {
        var subscription = SubscriptionWith(new MeterTopUpTerms
        {
            Enabled = false,
            UnitPrices = [new MeterTopUpPrice { CurrencyCode = "CHF", UnitAmountMinor = 10 }],
            MaxQuantity = 1_000
        });

        PricedMapper().ToResponse(subscription).Meters.Single().CreditPurchase.Should().BeNull();
    }

    // ---- discount scope -------------------------------------------------------------------

    [Fact]
    public async Task A_credit_purchase_code_cannot_run_for_several_periods()
    {
        var request = CreditCode();
        request.DurationPeriods = 3;

        var result = await new CreateDiscountRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(
            error => error.ErrorCode == "subscription_discount_credit_scope_invalid",
            "a purchase is one charge, so a duration would be silently meaningless (AC-29)");
    }

    [Fact]
    public async Task A_credit_purchase_code_cannot_be_a_subscription_campaign()
    {
        var request = CreditCode();
        request.CampaignKind = CampaignKind.FirstAnnualPeriod;

        var result = await new CreateDiscountRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error => error.ErrorCode == "subscription_discount_credit_scope_invalid");
    }

    [Fact]
    public async Task A_credit_purchase_code_cannot_be_limited_to_prices()
    {
        var request = CreditCode();
        request.ApplicablePriceIds = ["price-1"];

        var result = await new CreateDiscountRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(
            error => error.ErrorCode == "subscription_discount_credit_scope_invalid",
            "no credit purchase is charged against a subscription price, so it could never match");
    }

    [Fact]
    public async Task A_subscription_code_cannot_be_limited_to_meters()
    {
        var request = CreditCode();
        request.Scope = DiscountScope.Subscription;
        request.ApplicableMeterKeys = ["ai-credit"];

        var result = await new CreateDiscountRequestValidator().ValidateAsync(request);

        result.Errors.Should().Contain(error => error.ErrorCode == "subscription_discount_meter_scope_invalid");
    }

    [Fact]
    public async Task A_credit_purchase_code_may_be_limited_to_one_use_per_organization()
    {
        var request = CreditCode();
        request.OneUsePerOrganization = true;

        var result = await new CreateDiscountRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue("a once-per-customer promotional top-up is the ordinary thing to want");
    }

    [Fact]
    public async Task A_standard_subscription_code_still_cannot_be_one_use()
    {
        var request = CreditCode();
        request.Scope = DiscountScope.Subscription;
        request.ApplicableMeterKeys = [];
        request.OneUsePerOrganization = true;

        var result = await new CreateDiscountRequestValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse("the credit-scope exception must not widen what subscription codes accept");
    }

    [Fact]
    public void A_credit_purchase_code_is_refused_at_signup()
    {
        var discount = new Discount { Scope = DiscountScope.CreditPurchase };

        SubscriptionDiscountApplicability.Permits(discount, "pro", "price-1").Should().BeFalse(
            "a top-up code would otherwise discount every period of a subscription it was never priced for");
    }

    [Fact]
    public void A_subscription_code_is_refused_on_a_credit_purchase()
    {
        var discount = new Discount { Scope = DiscountScope.Subscription };

        SubscriptionDiscountApplicability.PermitsCreditPurchase(discount, "pro", "ai-credit")
            .Should().BeFalse("a subscription code may run for several periods and was not priced for credits (AC-3)");
    }

    [Fact]
    public void A_credit_code_limited_to_one_meter_applies_to_that_meter_only()
    {
        var discount = new Discount
        {
            Scope = DiscountScope.CreditPurchase,
            ApplicablePlanCodes = ["pro"],
            ApplicableMeterKeys = ["ai-credit"]
        };

        SubscriptionDiscountApplicability.PermitsCreditPurchase(discount, "pro", "ai-credit").Should().BeTrue();
        SubscriptionDiscountApplicability.PermitsCreditPurchase(discount, "pro", "storage").Should().BeFalse();
        SubscriptionDiscountApplicability.PermitsCreditPurchase(discount, "team", "ai-credit").Should().BeFalse(
            "plan and meter narrow together, not as two ways to qualify");
    }

    [Fact]
    public async Task A_credit_code_naming_a_meter_nobody_sells_credits_for_is_refused()
    {
        var request = CreditCode();
        request.ApplicableMeterKeys = ["storage"];

        var result = await CatalogueService(out _).CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse("a code no purchase could ever match would fail every customer silently");
        result.ErrorMessage.Should().Contain("storage");
    }

    [Fact]
    public async Task A_credit_code_is_stored_with_its_scope_and_meters()
    {
        var request = CreditCode();
        request.ApplicableMeterKeys = ["ai-credit"];
        request.OneUsePerOrganization = true;

        var result = await CatalogueService(out var created).CreateAsync(request, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        created().Scope.Should().Be(DiscountScope.CreditPurchase);
        created().ApplicableMeterKeys.Should().Equal("ai-credit");
        created().Campaign.OneUsePerOrganization.Should().BeTrue(
            "the one-use flag has to reach the redemption ledger, which reads it from the campaign terms");
        result.Value!.Scope.Should().Be("CreditPurchase");
    }

    // ---- helpers --------------------------------------------------------------------------

    private static MeterTopUpRequest TopUp() => new()
    {
        Enabled = true,
        UnitPrices = [new MeterTopUpPriceRequest { CurrencyCode = "CHF", UnitAmountMinor = 10 }],
        MinQuantity = 10,
        MaxQuantity = 10_000,
        ExpiryPolicy = MeterResetPolicy.CarryForward
    };

    private static UpdatePlanRequest PlanWith(MeterTopUpRequest topUp) => new()
    {
        DisplayName = "Pro",
        Meters =
        [
            new PlanMeterRequest
            {
                MeterKey = "ai-credit",
                DisplayName = "AI credits",
                UnitLabel = "credit",
                IncludedQuantity = 100,
                OverageAllowed = false,
                TopUp = topUp
            }
        ]
    };

    private static CreateDiscountRequest CreditCode() => new()
    {
        Code = "topup20",
        DisplayName = "Top-up 20%",
        Kind = DiscountKind.Percent,
        PercentBasisPoints = 2_000,
        Scope = DiscountScope.CreditPurchase
    };

    private static SubscriptionDetail SubscriptionWith(MeterTopUpTerms topUp) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Price = new PriceSnapshot { CurrencyCode = "CHF", Interval = BillingInterval.Month, IntervalCount = 1 },
        Plan = new PlanSnapshot
        {
            Code = "pro",
            DisplayName = "Pro",
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "ai-credit",
                    DisplayName = "AI credits",
                    UnitLabel = "credit",
                    IncludedQuantity = 100,
                    OverageAllowed = false,
                    TopUp = topUp
                }
            ]
        }
    };

    private static SubscriptionResponseMapper PricedMapper()
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options
            .SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions
            {
                CurrencyMinorUnits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CHF"] = 2,
                    ["USD"] = 2
                }
            });

        return new SubscriptionResponseMapper(
            new ControlledTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero)),
            new CurrencyMinorUnitResolver(options.Object));
    }

    private static DiscountCatalogueService CatalogueService(out Func<Discount> created)
    {
        var discounts = new Mock<ISubscriptionDiscountRepository>();
        var catalogue = new Mock<ISubscriptionCatalogueRepository>();
        var contextResolver = new Mock<ISubscriptionContextResolver>();
        Discount? stored = null;

        contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        catalogue
            .Setup(repository => repository.ListPlansAsync(
                TenantId, OrganizationId, It.IsAny<PlanCatalogueFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Plan
                {
                    ItemId = "plan-pro",
                    TenantId = TenantId,
                    Code = "pro",
                    DisplayName = "Pro",
                    Status = CatalogueStatus.Active,
                    Meters =
                    [
                        new PlanMeter
                        {
                            MeterKey = "ai-credit",
                            TopUp = new MeterTopUpTerms { Enabled = true, MaxQuantity = 1_000 }
                        },
                        new PlanMeter { MeterKey = "storage" }
                    ]
                }
            ]);

        discounts
            .Setup(repository => repository.TryCreateAsync(It.IsAny<Discount>(), It.IsAny<CancellationToken>()))
            .Callback<Discount, CancellationToken>((discount, _) => stored = discount)
            .ReturnsAsync(true);

        created = () => stored!;

        return new DiscountCatalogueService(
            discounts.Object,
            catalogue.Object,
            contextResolver.Object,
            new CreateDiscountRequestValidator(),
            new UpdateDiscountRequestValidator());
    }
}
