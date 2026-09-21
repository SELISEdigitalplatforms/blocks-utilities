using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// What an overage invoice tells the subscriber they were counted for, and at what rate.
/// </summary>
/// <remarks>
/// Guards the bug where a metered invoice arrived with its Qty and Unit price columns empty — a
/// total with nothing to check it against, which a subscriber cannot reconcile and a support agent
/// cannot explain. The rate bands are recomputed at issue time rather than stored, so the property
/// that matters most here is the negative one: when recomputation stops agreeing with what the
/// card was charged, the document says less rather than something untrue.
/// </remarks>
public sealed class MeteredInvoiceLineTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";
    private const string PeriodKey = "M20260901T000000Z";
    private const string MeterKey = "screening";

    private static readonly DateTime SettledAt = new(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc);

    private readonly FinancialDocumentLedgerFake _documents = new();
    private readonly Mock<IFinancialDocumentNumberAllocator> _numbers = new();
    private readonly Mock<ISubscriptionBillingProfileRepository> _profiles = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<ISubscriptionInvoiceHistoryRepository> _settledCharges = new();
    private readonly Mock<ISubscriptionMerchantProfileService> _merchants = new();
    private readonly Mock<ISubscriptionDocumentCursorRepository> _cursors = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _currency = new();
    private readonly Mock<ISubscriptionUsageInvoiceRepository> _usageInvoices = new();

    public MeteredInvoiceLineTests()
    {
        _numbers
            .Setup(numbers => numbers.AllocateAsync(
                TenantId,
                It.IsAny<FinancialDocumentType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("INV-2026-000001");

        _profiles
            .Setup(profiles => profiles.GetAsync(
                TenantId,
                OrganizationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionBillingProfile
            {
                TenantId = TenantId,
                OrganizationId = OrganizationId,
                LegalName = "Northwind Trading AG",
                BillingContactName = "Ada Byron",
                BillingContactEmail = "ada@northwind.example"
            });

        _merchants
            .Setup(merchants => merchants.ResolveAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialDocumentMerchant { LegalName = "Blocks AG" });

        _subscriptions
            .Setup(subscriptions => subscriptions.TryConsumeDocumentSourceAsync(
                TenantId,
                SubscriptionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _currency
            .Setup(currency => currency.TryConvert(
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                out It.Ref<long>.IsAny))
            .Returns((decimal amount, string _, out long minor) =>
            {
                minor = (long)(amount * 100);

                return true;
            });
    }

    [Fact]
    public async Task An_overage_line_states_the_units_counted_and_the_rate_they_were_counted_at()
    {
        // The reported case: one meter, one rate band, three units past the allowance at CHF 1.50.
        Subscribed(SingleBandMeter());
        UsageInvoiced(overageQuantity: 3, amountMinor: 450);
        SettledUsageCharge(grossMinor: 450, taxMinor: 36);

        var document = await Issue();

        document!.Lines.Should().ContainSingle(
            because: "one meter overran one rate band, so there is one thing to charge for");

        var line = document.Lines[0];

        line.Quantity.Should().Be(3m,
            because: "the subscriber is entitled to see how many units they were counted for");
        line.UnitAmountMinor.Should().Be(150,
            because: "CHF 1.50 is the rate the plan configures, and the invoice must name it");
        line.AmountMinor.Should().Be(450,
            because: "quantity times unit price has to produce the amount, or the line is unreadable");
        line.Description.Should().Contain("Screenings",
            because: "a subscriber with several meters has to tell their lines apart");
    }

    /// <summary>
    /// "Metered usage" read as a bill for every unit used. The line has to say it charges only what
    /// went past the allowance, and how far past.
    /// </summary>
    [Fact]
    public async Task An_overage_line_says_it_is_overage_beyond_what_the_plan_included()
    {
        Subscribed(SingleBandMeter());
        UsageInvoiced(overageQuantity: 3, amountMinor: 450, includedQuantity: 550.55m, usedQuantity: 553.55m);
        SettledUsageCharge(grossMinor: 450, taxMinor: 36);

        var document = await Issue();

        document!.Lines.Single().Description.Should().StartWith("Overage on ")
            .And.EndWith(": usage beyond the 550.55 included (553.55 used)");
    }

    /// <summary>
    /// A usage invoice rated before the allowance was recorded still says what it is, without
    /// figures it would have to reconstruct from today's plan.
    /// </summary>
    [Fact]
    public async Task An_overage_line_rated_before_the_allowance_was_recorded_still_says_it_is_overage()
    {
        Subscribed(SingleBandMeter());
        UsageInvoiced(overageQuantity: 3, amountMinor: 450);
        SettledUsageCharge(grossMinor: 450, taxMinor: 36);

        var document = await Issue();

        document!.Lines.Single().Description.Should().StartWith("Overage on ")
            .And.EndWith(": usage beyond the included allowance");
    }

    [Fact]
    public async Task Each_rate_band_an_overage_crossed_gets_its_own_line_at_its_own_rate()
    {
        // 600 units against "first 500 at CHF 1.50, thereafter CHF 1.00": no single unit price can
        // describe this, so stating one would be an invention. Two bands, two rates.
        Subscribed(GraduatedMeter());
        UsageInvoiced(overageQuantity: 600, amountMinor: 85_000);
        SettledUsageCharge(grossMinor: 85_000, taxMinor: 6_885);

        var document = await Issue();

        document!.Lines.Should().HaveCount(2,
            because: "the overage crossed a band boundary and each band was priced differently");

        document.Lines[0].Quantity.Should().Be(500m);
        document.Lines[0].UnitAmountMinor.Should().Be(150);
        document.Lines[0].AmountMinor.Should().Be(75_000);

        document.Lines[1].Quantity.Should().Be(100m);
        document.Lines[1].UnitAmountMinor.Should().Be(100);
        document.Lines[1].AmountMinor.Should().Be(10_000);

        document.Lines.Sum(line => line.AmountMinor).Should().Be(
            document.Amounts.GrossSubtotalMinor,
            because: "lines that do not add up to the subtotal make the invoice unauditable");

        document.Lines[0].Description.Should().Contain("1–500",
            because: "two lines for one meter are only tellable apart by the units they cover");
        document.Lines[1].Description.Should().Contain("501–600");
    }

    [Fact]
    public async Task A_fractional_meter_reports_the_fraction_it_counted()
    {
        // A meter authored to count in hundredths. Truncating 2.5 to 2 on the document would have
        // the invoice disagree with its own total.
        Subscribed(SingleBandMeter(quantityScale: 2));
        UsageInvoiced(overageQuantity: 2.5m, amountMinor: 375);
        SettledUsageCharge(grossMinor: 375, taxMinor: 30);

        var document = await Issue();

        document!.Lines.Should().ContainSingle();
        document.Lines[0].Quantity.Should().Be(2.5m,
            because: "a meter that counts in fractions is billed in fractions");
        document.Lines[0].UnitAmountMinor.Should().Be(150);
        document.Lines[0].AmountMinor.Should().Be(375);
    }

    [Fact]
    public async Task A_rate_changed_after_the_charge_leaves_the_invoice_saying_only_what_it_took()
    {
        // The plan was re-rated between rating and issuance, so recomputing the bands now would
        // price the line at CHF 2.00 against a card that was charged at CHF 1.50. The document has
        // to decline to break the charge down rather than describe it wrongly.
        Subscribed(SingleBandMeter(unitAmountMinor: 200));
        UsageInvoiced(overageQuantity: 3, amountMinor: 450);
        SettledUsageCharge(grossMinor: 450, taxMinor: 36);

        var document = await Issue();

        document!.Lines.Should().ContainSingle();
        document.Lines[0].Quantity.Should().BeNull(
            because: "no quantity is better than one multiplied by a rate nobody was charged");
        document.Lines[0].UnitAmountMinor.Should().BeNull();
        document.Lines[0].AmountMinor.Should().Be(
            document.Amounts.NetSubtotalMinor,
            because: "the total the subscriber paid is still owed to them in full");
    }

    [Fact]
    public async Task A_usage_invoice_that_cannot_be_found_still_produces_a_document()
    {
        // The breakdown is a courtesy; the document is an obligation. Losing the first must never
        // cost the second.
        Subscribed(SingleBandMeter());
        _usageInvoices
            .Setup(invoices => invoices.GetAsync(
                TenantId,
                SubscriptionId,
                PeriodKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionUsageInvoice?)null);
        SettledUsageCharge(grossMinor: 450, taxMinor: 36);

        var document = await Issue();

        document.Should().NotBeNull(
            because: "an invoice is owed for money that moved, breakdown or no breakdown");
        document!.Lines.Should().ContainSingle();
        document.Lines[0].Quantity.Should().BeNull();
    }

    private Task<SubscriptionFinancialDocument?> Issue() =>
        Issuer().IssueDocumentForPaymentAsync(
            TenantId,
            "pay-1",
            "corr-1",
            CancellationToken.None);

    private ISubscriptionFinancialDocumentIssuer Issuer() =>
        new SubscriptionFinancialDocumentIssuer(
            _documents,
            _numbers.Object,
            _profiles.Object,
            _merchants.Object,
            _subscriptions.Object,
            _payments.Object,
            _settledCharges.Object,
            _cursors.Object,
            _currency.Object,
            Options.Create(new SubscriptionOptions
            {
                Invoicing = new SubscriptionInvoicingOptions { LegalName = "Blocks AG" }
            }),
            NullLogger<SubscriptionFinancialDocumentIssuer>.Instance,
            scheduler: null,
            usageInvoices: _usageInvoices.Object);

    /// <summary>One unbounded band: the shape almost every plan is actually authored in.</summary>
    private static PlanMeter SingleBandMeter(
        long unitAmountMinor = 150,
        int quantityScale = 0) =>
        new()
        {
            MeterKey = MeterKey,
            DisplayName = "Screenings",
            UnitLabel = "screening",
            QuantityScale = quantityScale,
            RateTables =
            [
                new MeterRateTable
                {
                    CurrencyCode = "CHF",
                    Tiers = [new MeterTier { UpToQuantity = null, UnitAmountMinor = unitAmountMinor }]
                }
            ]
        };

    /// <summary>First 500 at CHF 1.50, everything after at CHF 1.00.</summary>
    private static PlanMeter GraduatedMeter() =>
        new()
        {
            MeterKey = MeterKey,
            DisplayName = "Screenings",
            UnitLabel = "screening",
            RateTables =
            [
                new MeterRateTable
                {
                    CurrencyCode = "CHF",
                    Tiers =
                    [
                        new MeterTier { UpToQuantity = 500, UnitAmountMinor = 150 },
                        new MeterTier { UpToQuantity = null, UnitAmountMinor = 100 }
                    ]
                }
            ]
        };

    private void Subscribed(PlanMeter meter)
    {
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            CurrencyCode = "CHF",
            Status = SubscriptionStatus.Active,
            Plan = new PlanSnapshot
            {
                Code = "pro",
                DisplayName = "Pro",
                Meters = [meter]
            },
            Price = new PriceSnapshot
            {
                PriceId = "price-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 100_000,
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                TaxRateBasisPoints = 810,
                TaxMode = TaxMode.Exclusive
            },
            FeeSchedule = new BillingSchedule
            {
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                AnchorInstantUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                TimeZoneId = "Europe/Zurich",
                AnchorDayOfMonth = 1
            },
            UsageSchedule = new BillingSchedule
            {
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                AnchorInstantUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                TimeZoneId = "Europe/Zurich",
                AnchorDayOfMonth = 1
            },
            CurrentPeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentUsagePeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentUsagePeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _subscriptions
            .Setup(subscriptions => subscriptions.GetByIdAsync(
                TenantId,
                SubscriptionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
    }

    private void UsageInvoiced(
        decimal overageQuantity,
        long amountMinor,
        decimal? includedQuantity = null,
        decimal? usedQuantity = null) =>
        _usageInvoices
            .Setup(invoices => invoices.GetAsync(
                TenantId,
                SubscriptionId,
                PeriodKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageInvoice
            {
                TenantId = TenantId,
                OrganizationId = OrganizationId,
                SubscriptionId = SubscriptionId,
                PeriodKey = PeriodKey,
                CurrencyCode = "CHF",
                TotalAmountMinor = amountMinor,
                NetAmountMinor = amountMinor,
                State = SubscriptionUsageInvoiceState.Charged,
                Lines =
                [
                    new UsageInvoiceLine
                    {
                        MeterKey = MeterKey,
                        OverageQuantity = overageQuantity,
                        IncludedQuantity = includedQuantity,
                        UsedQuantity = usedQuantity,
                        AmountMinor = amountMinor
                    }
                ]
            });

    private void SettledUsageCharge(long grossMinor, long taxMinor) =>
        _payments
            .Setup(payments => payments.GetByIdAsync(
                TenantId,
                "pay-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-1",
                TenantId = TenantId,
                CustomerOrganizationId = OrganizationId,
                PaymentStatus = PaymentStatuses.Captured,
                PaymentFlow = PaymentFlows.SubscriptionInvoice,
                PaymentDate = SettledAt,
                CurrencyCode = "CHF",
                PreciseAmount = (grossMinor + taxMinor) / 100m,
                OrderId = SubscriptionConstants.UsageInvoiceOrderIdFor(SubscriptionId, PeriodKey),
                UserId = "user-7",
                SubscriptionGrossAmountMinor = grossMinor,
                SubscriptionNetAmountMinor = grossMinor,
                SubscriptionTaxAmountMinor = taxMinor,
                SubscriptionTaxRateBasisPoints = 810,
                SubscriptionTaxMode = nameof(TaxMode.Exclusive)
            });
}
