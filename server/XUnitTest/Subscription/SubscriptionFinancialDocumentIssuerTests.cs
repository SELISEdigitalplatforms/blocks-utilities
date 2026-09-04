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
/// Turning something that happened to money into a document that says so.
/// </summary>
/// <remarks>
/// The properties worth having tests for are all about what must <em>not</em> happen: a second
/// document for one payment, a document for a charge that never settled, an invoice for another
/// product's payment, a credit note that does not add back to the invoice it adjusts.
/// </remarks>
public sealed class SubscriptionFinancialDocumentIssuerTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";

    private static readonly DateTime SettledAt = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    private readonly FinancialDocumentLedgerFake _documents = new();
    private readonly Mock<IFinancialDocumentNumberAllocator> _numbers = new();
    private readonly Mock<ISubscriptionBillingProfileRepository> _profiles = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<ISubscriptionInvoiceHistoryRepository> _settledCharges = new();
    private readonly Mock<ISubscriptionMerchantProfileService> _merchants = new();
    private readonly Mock<ISubscriptionDocumentCursorRepository> _cursors = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _currency = new();

    /// <summary>The obligations a transition would have appended, keyed by source key.</summary>
    private readonly Dictionary<string, SubscriptionDocumentSource> _consumed = [];

    private int _allocations;

    public SubscriptionFinancialDocumentIssuerTests()
    {
        _numbers
            .Setup(numbers => numbers.AllocateAsync(
                TenantId,
                It.IsAny<FinancialDocumentType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, FinancialDocumentType type, int year, CancellationToken _) =>
            {
                _allocations++;

                return $"{(type == FinancialDocumentType.CreditNote ? "CRN" : "INV")}-{year}-" +
                    $"{_allocations:D6}";
            });

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
                BillingContactEmail = "ada@northwind.example",
                Contacts =
                [
                    new BillingContact
                    {
                        UserId = "user-7",
                        Name = "Grace Hopper",
                        Email = "grace@northwind.example"
                    }
                ]
            });

        // Nothing owing unless a test says so. Moq would otherwise hand back a null list and the
        // recovery pass would fail on it for a reason that has nothing to do with what is under test.
        _subscriptions
            .Setup(subscriptions => subscriptions.ListWithPendingDocumentSourcesAsync(
                TenantId,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _subscriptions
            .Setup(subscriptions => subscriptions.ListTrialsStartedSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _merchants
            .Setup(merchants => merchants.ResolveAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialDocumentMerchant { LegalName = "Blocks AG" });

        // Records what the issuer cleared, which is the only way to see that an obligation was
        // discharged rather than left for the sweep to find again forever.
        _subscriptions
            .Setup(subscriptions => subscriptions.TryConsumeDocumentSourceAsync(
                TenantId,
                SubscriptionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string sourceKey, CancellationToken _) =>
                _consumed.Remove(sourceKey));

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
    public async Task A_settled_renewal_becomes_an_invoice_carrying_the_breakdown_it_was_charged_on()
    {
        Subscribed();
        SettledRenewal();

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId,
            "pay-1",
            "corr-1",
            CancellationToken.None);

        document.Should().NotBeNull();
        document!.DocumentType.Should().Be(FinancialDocumentType.Invoice);
        document.DocumentNumber.Should().Be("INV-2026-000001");
        document.PaymentDetailId.Should().Be("pay-1");
        document.SubscriptionId.Should().Be(SubscriptionId);

        // The figures come off the payment verbatim, and the total is net plus tax less credit —
        // which is what the provider actually took.
        document.Amounts.GrossSubtotalMinor.Should().Be(100_000);
        document.Amounts.AutomaticDiscountMinor.Should().Be(8_000);
        document.Amounts.PromotionalDiscountMinor.Should().Be(2_000);
        document.Amounts.NetSubtotalMinor.Should().Be(90_000);
        document.Amounts.TaxAmountMinor.Should().Be(6_930);
        document.Amounts.CreditAppliedMinor.Should().Be(1_000);
        document.Amounts.TotalMinor.Should().Be(95_930);

        // Dated by the payment rather than by this bookkeeping step, so a charge settled in December
        // and documented in January stays in December's numbers.
        document.IssuedAtUtc.Should().Be(SettledAt);
    }

    [Fact]
    public async Task An_automatic_discount_and_a_volume_band_appear_as_two_lines_not_one()
    {
        Subscribed();

        // Additive: 8% and 5% were summed into one 13% rate, so the money divides in proportion to
        // the parts that were summed. 13,000 split 8:5 is 8,000 and 5,000.
        SettledRenewal(payment =>
        {
            payment.SubscriptionBuiltInDiscountMinor = 13_000;
            payment.SubscriptionPromotionalDiscountMinor = 0;
            payment.SubscriptionAutomaticDiscountBasisPoints = 800;
            payment.SubscriptionQuantityDiscountBasisPoints = 500;
            payment.SubscriptionDiscountCombination =
                nameof(AutomaticDiscountCombination.Additive);
            payment.SubscriptionNetAmountMinor = 87_000;
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.Amounts.AutomaticDiscountMinor.Should().Be(8_000);
        document.Amounts.QuantityDiscountMinor.Should().Be(5_000);

        // Whatever the split, the two lines must add back to what came off.
        (document.Amounts.AutomaticDiscountMinor + document.Amounts.QuantityDiscountMinor)
            .Should().Be(13_000);
    }

    [Fact]
    public async Task Under_best_discount_the_losing_source_shows_nothing()
    {
        Subscribed();

        // The band's 5% beat the price's 3%, so the whole reduction belongs to the band. Reporting
        // any of it against the price would claim a discount the subscriber did not get.
        SettledRenewal(payment =>
        {
            payment.SubscriptionBuiltInDiscountMinor = 5_000;
            payment.SubscriptionPromotionalDiscountMinor = 0;
            payment.SubscriptionAutomaticDiscountBasisPoints = 300;
            payment.SubscriptionQuantityDiscountBasisPoints = 500;
            payment.SubscriptionDiscountCombination =
                nameof(AutomaticDiscountCombination.BestDiscount);
            payment.SubscriptionNetAmountMinor = 95_000;
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.Amounts.AutomaticDiscountMinor.Should().Be(0);
        document.Amounts.QuantityDiscountMinor.Should().Be(5_000);
    }

    [Fact]
    public async Task Issuing_the_same_payment_twice_allocates_one_number_and_one_document()
    {
        Subscribed();
        SettledRenewal();

        var issuer = Issuer();
        var first = await issuer.IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);
        var second = await issuer.IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-2", CancellationToken.None);

        // The property the whole design turns on. A redelivered webhook, a retried work item and a
        // recovery sweep all land here, and the second must find the first rather than add to it.
        second!.ItemId.Should().Be(first!.ItemId);
        second.DocumentNumber.Should().Be(first.DocumentNumber);
        _documents.Documents.Should().HaveCount(1);
        _allocations.Should().Be(1);
    }

    [Fact]
    public async Task A_settlement_invoice_carries_its_two_sided_breakdown()
    {
        Subscribed();
        SettledRenewal(payment =>
        {
            payment.OrderId = SubscriptionConstants.SettlementOrderIdFor(
                SubscriptionId,
                SettlementReservationKind.PlanChange,
                "res-9");
            payment.PreciseAmount = 60.00m;
            // Production never writes flat fields beside a settlement — see
            // StripeInvoiceBillingGateway.NewPayment. A test that leaves SettledRenewal's flat 90,000
            // in place would pass even if the issuer read the wrong branch, because it never has to
            // fall back to the settlement at all.
            payment.SubscriptionGrossAmountMinor = null;
            payment.SubscriptionBuiltInDiscountMinor = null;
            payment.SubscriptionPromotionalDiscountMinor = null;
            payment.SubscriptionNetAmountMinor = null;
            payment.SubscriptionTaxAmountMinor = null;
            payment.SubscriptionCreditAmountMinor = null;
            payment.SubscriptionSettlement = new SubscriptionSettlementBreakdown
            {
                Outgoing = new SubscriptionSettlementSide
                {
                    TaxAmountMinor = 540,
                    PeriodTotalMinor = 6_000,
                    ProratedValueMinor = 3_000
                },
                Target = new SubscriptionSettlementSide
                {
                    TaxAmountMinor = 1_620,
                    PeriodTotalMinor = 18_000,
                    ProratedValueMinor = 9_000
                },
                CreditConsumedMinor = 0,
                NetSettlementMinor = 6_000
            };
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        // A settlement is a subtraction between two prorated periods, and a single subtotal cannot
        // explain one. The invoice has to carry both sides or the subscriber cannot check it.
        document!.Settlement.Should().NotBeNull();
        document.Settlement!.NetSettlementMinor.Should().Be(6_000);
        document.Lines.Should().ContainSingle();

        // Both sides carry a real period and tax, so this exercises the main split rather than the
        // empty-breakdown fallback: net 5,460 and tax 540 out of the 6,000 total.
        document.Amounts.NetSubtotalMinor.Should().Be(5_460);
        document.Amounts.TaxAmountMinor.Should().Be(540);
    }

    [Fact]
    public async Task A_settlement_invoice_totals_what_the_provider_took_not_zero()
    {
        Subscribed();
        SettledRenewal(payment =>
        {
            payment.OrderId = SubscriptionConstants.SettlementOrderIdFor(
                SubscriptionId,
                SettlementReservationKind.PlanChange,
                "res-9");
            payment.PreciseAmount = 5.17m;
            payment.SubscriptionGrossAmountMinor = null;
            payment.SubscriptionBuiltInDiscountMinor = null;
            payment.SubscriptionPromotionalDiscountMinor = null;
            payment.SubscriptionNetAmountMinor = null;
            payment.SubscriptionTaxAmountMinor = null;
            payment.SubscriptionCreditAmountMinor = null;
            payment.SubscriptionSettlement = SettlementBreakdown();
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.Amounts.TotalMinor.Should().Be(517);
        document.Amounts.NetSubtotalMinor.Should().Be(470);
        document.Amounts.TaxAmountMinor.Should().Be(47);
        document.Amounts.GrossSubtotalMinor.Should().Be(470);
        (document.Amounts.NetSubtotalMinor + document.Amounts.TaxAmountMinor -
            document.Amounts.CreditAppliedMinor).Should().Be(document.Amounts.TotalMinor);
    }

    [Fact]
    public async Task A_settlement_invoice_ignores_a_zero_net_stored_beside_its_breakdown()
    {
        // Every settlement payment row written before this fix stores a flat net/tax/credit of 0
        // beside its breakdown. Without the settlement branch running first, this reproduces the
        // reported bug exactly: a real charge that reads back as CHF 0.00.
        Subscribed();
        SettledRenewal(payment =>
        {
            payment.OrderId = SubscriptionConstants.SettlementOrderIdFor(
                SubscriptionId,
                SettlementReservationKind.PlanChange,
                "res-9");
            payment.PreciseAmount = 5.17m;
            payment.SubscriptionGrossAmountMinor = null;
            payment.SubscriptionBuiltInDiscountMinor = null;
            payment.SubscriptionPromotionalDiscountMinor = null;
            payment.SubscriptionNetAmountMinor = 0;
            payment.SubscriptionTaxAmountMinor = 0;
            payment.SubscriptionCreditAmountMinor = 0;
            payment.SubscriptionSettlement = SettlementBreakdown();
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.Amounts.TotalMinor.Should().Be(517);
        document.Amounts.NetSubtotalMinor.Should().Be(470);
        document.Amounts.TaxAmountMinor.Should().Be(47);
        document.Amounts.GrossSubtotalMinor.Should().Be(470);
    }

    [Fact]
    public async Task A_settlement_invoices_line_states_the_net_difference()
    {
        Subscribed();
        SettledRenewal(payment =>
        {
            payment.OrderId = SubscriptionConstants.SettlementOrderIdFor(
                SubscriptionId,
                SettlementReservationKind.PlanChange,
                "res-9");
            payment.PreciseAmount = 5.17m;
            payment.SubscriptionGrossAmountMinor = null;
            payment.SubscriptionBuiltInDiscountMinor = null;
            payment.SubscriptionPromotionalDiscountMinor = null;
            payment.SubscriptionNetAmountMinor = null;
            payment.SubscriptionTaxAmountMinor = null;
            payment.SubscriptionCreditAmountMinor = null;
            payment.SubscriptionSettlement = SettlementBreakdown();
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.Lines.Should().ContainSingle();
        document.Lines[0].AmountMinor.Should().Be(document.Amounts.NetSubtotalMinor);
        document.Lines[0].AmountMinor.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_settlement_that_spent_credit_shows_it_below_tax()
    {
        Subscribed();
        SettledRenewal(payment =>
        {
            payment.OrderId = SubscriptionConstants.SettlementOrderIdFor(
                SubscriptionId,
                SettlementReservationKind.PlanChange,
                "res-9");
            payment.PreciseAmount = 3.17m;
            payment.SubscriptionGrossAmountMinor = null;
            payment.SubscriptionBuiltInDiscountMinor = null;
            payment.SubscriptionPromotionalDiscountMinor = null;
            payment.SubscriptionNetAmountMinor = null;
            payment.SubscriptionTaxAmountMinor = null;
            payment.SubscriptionCreditAmountMinor = null;

            var settlement = SettlementBreakdown();
            settlement.CreditConsumedMinor = 200;
            settlement.NetSettlementMinor -= 200;
            payment.SubscriptionSettlement = settlement;
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.Amounts.CreditAppliedMinor.Should().Be(200);
        document.Amounts.TotalMinor.Should().Be(317);
        // Credit pays the bill rather than shrinking what tax is calculated on.
        (document.Amounts.NetSubtotalMinor + document.Amounts.TaxAmountMinor).Should().Be(517);
    }

    [Fact]
    public async Task An_opening_stub_upgrade_counts_both_periods_and_the_credit_once()
    {
        Subscribed();
        SettledRenewal(payment =>
        {
            payment.OrderId = SubscriptionConstants.SettlementOrderIdFor(
                SubscriptionId,
                SettlementReservationKind.PlanChange,
                "res-9");
            // 940 net + 94 tax - 200 credit = 834, the total the provider actually took.
            payment.PreciseAmount = 8.34m;
            payment.SubscriptionGrossAmountMinor = null;
            payment.SubscriptionBuiltInDiscountMinor = null;
            payment.SubscriptionPromotionalDiscountMinor = null;
            payment.SubscriptionNetAmountMinor = null;
            payment.SubscriptionTaxAmountMinor = null;
            payment.SubscriptionCreditAmountMinor = null;

            var settlement = SettlementBreakdown();
            settlement.CreditConsumedMinor = 200;
            settlement.NetSettlementMinor = 834;
            // A different, larger credit on the nested breakdown. Only the top-level 200 is ever
            // spent — the nested figure is the annual side's own contribution for the invoice to
            // explain, not a second deduction — so a bug that summed the two would produce 500 here
            // instead of 200 and this test would catch it.
            settlement.Annual = SettlementBreakdown();
            settlement.Annual.CreditConsumedMinor = 300;
            payment.SubscriptionSettlement = settlement;
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.Amounts.CreditAppliedMinor.Should().Be(200);
        document.Amounts.NetSubtotalMinor.Should().Be(940);
        document.Amounts.TaxAmountMinor.Should().Be(94);
        document.Amounts.TotalMinor.Should().Be(834);
        (document.Amounts.NetSubtotalMinor + document.Amounts.TaxAmountMinor -
            document.Amounts.CreditAppliedMinor).Should().Be(document.Amounts.TotalMinor);
    }

    [Fact]
    public async Task A_settlement_with_an_empty_breakdown_reports_the_charge_untaxed()
    {
        Subscribed();
        SettledRenewal(payment =>
        {
            payment.OrderId = SubscriptionConstants.SettlementOrderIdFor(
                SubscriptionId,
                SettlementReservationKind.PlanChange,
                "res-9");
            payment.PreciseAmount = 5.17m;
            payment.SubscriptionGrossAmountMinor = null;
            payment.SubscriptionBuiltInDiscountMinor = null;
            payment.SubscriptionPromotionalDiscountMinor = null;
            payment.SubscriptionNetAmountMinor = null;
            payment.SubscriptionTaxAmountMinor = null;
            payment.SubscriptionCreditAmountMinor = null;
            payment.SubscriptionSettlement = new SubscriptionSettlementBreakdown
            {
                Outgoing = new SubscriptionSettlementSide(),
                Target = new SubscriptionSettlementSide(),
                CreditConsumedMinor = 0,
                NetSettlementMinor = 517
            };
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.Amounts.TotalMinor.Should().Be(517);
        document.Amounts.NetSubtotalMinor.Should().Be(517);
        document.Amounts.TaxAmountMinor.Should().Be(0);
    }

    [Fact]
    public async Task A_quantity_change_settlement_is_read_the_same_way_as_a_plan_change()
    {
        Subscribed();
        SettledRenewal(payment =>
        {
            payment.OrderId = SubscriptionConstants.SettlementOrderIdFor(
                SubscriptionId,
                SettlementReservationKind.QuantityIncrease,
                "res-9");
            payment.PreciseAmount = 5.17m;
            payment.SubscriptionGrossAmountMinor = null;
            payment.SubscriptionBuiltInDiscountMinor = null;
            payment.SubscriptionPromotionalDiscountMinor = null;
            payment.SubscriptionNetAmountMinor = null;
            payment.SubscriptionTaxAmountMinor = null;
            payment.SubscriptionCreditAmountMinor = null;
            payment.SubscriptionSettlement = SettlementBreakdown();
        });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.Amounts.TotalMinor.Should().Be(517);
        document.Amounts.NetSubtotalMinor.Should().Be(470);
        document.Amounts.TaxAmountMinor.Should().Be(47);
    }

    /// <summary>
    /// The outgoing/target sides used across the settlement tests above: net 517 overall.
    /// </summary>
    /// <remarks>
    /// Gross less discounts (none here) equals period total less tax on each side, the identity a
    /// real settlement side always satisfies — Gross 900 = PeriodTotal 990 - Tax 90, and Gross 1,840 =
    /// PeriodTotal 2,024 - Tax 184.
    /// </remarks>
    private static SubscriptionSettlementBreakdown SettlementBreakdown() => new()
    {
        Outgoing = new SubscriptionSettlementSide
        {
            GrossAmountMinor = 900,
            TaxAmountMinor = 90,
            PeriodTotalMinor = 990,
            ProratedValueMinor = 495
        },
        Target = new SubscriptionSettlementSide
        {
            GrossAmountMinor = 1_840,
            TaxAmountMinor = 184,
            PeriodTotalMinor = 2_024,
            ProratedValueMinor = 1_012
        },
        CreditConsumedMinor = 0,
        NetSettlementMinor = 517
    };

    [Theory]
    [InlineData(PaymentStatuses.Refused)]
    [InlineData(PaymentStatuses.Processing)]
    [InlineData(PaymentStatuses.Authorized)]
    [InlineData(PaymentStatuses.Cancelled)]
    public async Task A_charge_that_never_settled_produces_no_document(string status)
    {
        Subscribed();
        SettledRenewal(payment => payment.PaymentStatus = status);

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        // Revenue in the ledger that the bank never saw is worse than a missing invoice.
        document.Should().BeNull();
        _documents.Documents.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_products_payment_in_the_same_tenant_is_left_alone()
    {
        Subscribed();
        SettledRenewal(payment => payment.OrderId = "shop-order-42");

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document.Should().BeNull();
    }

    [Fact]
    public async Task Every_trial_gets_one_zero_total_document_stating_its_terms()
    {
        var subscription = Subscribed(item => item.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            RequiresPaymentMethod = false
        });

        Owing(
            subscription,
            SubscriptionDocumentSourceFactory.ForTrial(subscription, null, "corr-1")!);

        var issuer = Issuer();
        (await issuer.IssueForSubscriptionAsync(
            TenantId, SubscriptionId, "corr-1", CancellationToken.None))
            .Should().Be(1);

        var document = _documents.Documents.Single();
        document.DocumentType.Should().Be(FinancialDocumentType.TrialInvoice);
        document.Amounts.TotalMinor.Should().Be(0);
        document.Amounts.NetSubtotalMinor.Should().Be(0);
        document.Trial!.RequiresPaymentMethod.Should().BeFalse();
        document.Trial.StartsAtUtc.Should().Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        // Numbered in the invoice series, not a third one: a subscriber whose first document is
        // INV-2026-000001 can tell they have all of them.
        document.DocumentNumber.Should().StartWith("INV-");

        // And exactly one, however many times the trial is announced. The obligation is cleared by
        // the first pass, so the second finds nothing to do — and even a source that survived would
        // land on the same document, because the key is derived from the trial rather than generated.
        Owing(
            subscription,
            SubscriptionDocumentSourceFactory.ForTrial(subscription, null, "corr-2")!);

        await issuer.IssueForSubscriptionAsync(
            TenantId, SubscriptionId, "corr-2", CancellationToken.None);

        _documents.Documents.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_subscription_with_no_trial_gets_no_trial_invoice()
    {
        var subscription = Subscribed();

        // An obligation naming a trial the subscription does not have. Discarded rather than retried
        // forever: there is no document it could describe.
        Owing(subscription, new SubscriptionDocumentSource
        {
            SourceKey = "trial:sub-1:2026-08-01T00:00:00.0000000Z",
            DocumentType = FinancialDocumentType.TrialInvoice
        });

        (await Issuer().IssueForSubscriptionAsync(
            TenantId, SubscriptionId, "corr-1", CancellationToken.None))
            .Should().Be(0);

        _documents.Documents.Should().BeEmpty();
        _consumed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_full_refund_reverses_exactly_what_was_charged_and_links_to_its_invoice()
    {
        Subscribed();
        SettledRenewal();

        var issuer = Issuer();
        var invoice = await issuer.IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        RefundedBy(959.30m, PaymentStatuses.Refunded, refundedTotal: 959.30m);

        var creditNote = await issuer.IssueRefundCreditNoteAsync(
            TenantId, "pay-1", "refund-1", "corr-2", CancellationToken.None);

        creditNote.Should().NotBeNull();
        creditNote!.DocumentType.Should().Be(FinancialDocumentType.CreditNote);
        creditNote.DocumentNumber.Should().StartWith("CRN-");
        creditNote.OriginalDocumentId.Should().Be(invoice!.ItemId);
        creditNote.OriginalDocumentNumber.Should().Be(invoice.DocumentNumber);

        // Returning everything reverses everything, with no rounding involved at all.
        creditNote.Amounts.TotalMinor.Should().Be(invoice.Amounts.TotalMinor);
        creditNote.Amounts.TaxAmountMinor.Should().Be(invoice.Amounts.TaxAmountMinor);
        creditNote.Amounts.NetSubtotalMinor.Should().Be(invoice.Amounts.NetSubtotalMinor);

        // And the invoice says so, so a list can render a badge without joining.
        _documents.Documents
            .Single(document => document.ItemId == invoice.ItemId)
            .Status.Should().Be(FinancialDocumentStatus.Refunded);
    }

    [Fact]
    public async Task A_partial_refund_allocates_every_figure_so_the_credit_note_reconciles()
    {
        Subscribed();
        SettledRenewal();

        var issuer = Issuer();
        var invoice = await issuer.IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        // A third of the charge, chosen because it does not divide evenly into anything.
        RefundedBy(319.77m, PaymentStatuses.PartiallyRefunded, refundedTotal: 319.77m);

        var creditNote = await issuer.IssueRefundCreditNoteAsync(
            TenantId, "pay-1", "refund-1", "corr-2", CancellationToken.None);

        var amounts = creditNote!.Amounts;

        // The two properties that make it a usable document: the total is exactly what was returned,
        // and net plus tax adds back to it. The naive implementation — recalculate tax on the
        // refunded amount — misses the second by a minor unit or two.
        amounts.TotalMinor.Should().Be(31_977);
        (amounts.NetSubtotalMinor + amounts.TaxAmountMinor).Should().Be(31_977);

        // Gross less the discounts still equals net, as it did on the invoice.
        (amounts.GrossSubtotalMinor
            - amounts.AutomaticDiscountMinor
            - amounts.QuantityDiscountMinor
            - amounts.PromotionalDiscountMinor)
            .Should().Be(amounts.NetSubtotalMinor);

        // Every reversal is a proportion of the original, never more than it.
        amounts.TaxAmountMinor.Should().BeLessThan(invoice!.Amounts.TaxAmountMinor);
        _documents.Documents
            .Single(document => document.ItemId == invoice.ItemId)
            .Status.Should().Be(FinancialDocumentStatus.PartiallyRefunded);
    }

    [Fact]
    public async Task A_refund_that_has_not_confirmed_credits_nothing()
    {
        Subscribed();
        SettledRenewal();
        RefundedBy(100m, PaymentStatuses.PartiallyRefunded, refundedTotal: 100m,
            refundStatus: PaymentRefundStatuses.Submitted);

        var creditNote = await Issuer().IssueRefundCreditNoteAsync(
            TenantId, "pay-1", "refund-1", "corr-2", CancellationToken.None);

        // A submitted refund has returned no money. A credit note for it would be a promise the bank
        // did not keep.
        creditNote.Should().BeNull();
    }

    [Fact]
    public async Task The_same_refund_credits_once_however_often_it_is_seen()
    {
        Subscribed();
        SettledRenewal();
        RefundedBy(959.30m, PaymentStatuses.Refunded, refundedTotal: 959.30m);

        var issuer = Issuer();
        var first = await issuer.IssueRefundCreditNoteAsync(
            TenantId, "pay-1", "refund-1", "corr-1", CancellationToken.None);
        var second = await issuer.IssueRefundCreditNoteAsync(
            TenantId, "pay-1", "refund-1", "corr-2", CancellationToken.None);

        second!.ItemId.Should().Be(first!.ItemId);
        _documents.Documents.Should().ContainSingle();
    }

    [Fact]
    public async Task A_downgrade_that_banks_credit_gets_a_credit_note_for_what_it_banked()
    {
        var subscription = Subscribed();

        Owing(
            subscription,
            LegacyBankedCreditSource(
                subscription,
                "v4",
                creditedMinor: 4_250,
                settlement: new SubscriptionSettlementBreakdown { NetSettlementMinor = -4_250 },
                initiatedByUserId: "user-7",
                occurredAtUtc: SettledAt,
                "corr-1")!);

        (await Issuer().IssueForSubscriptionAsync(
            TenantId, SubscriptionId, "corr-1", CancellationToken.None))
            .Should().Be(1);

        var document = _documents.Documents.Single();
        document.DocumentType.Should().Be(FinancialDocumentType.CreditNote);
        document.Amounts.TotalMinor.Should().Be(4_250);
        document.SettlementReservationId.Should().Be("v4");

        // Named, because a person asked for the downgrade. A renewal would say "System renewal".
        document.InitiatedBy.Name.Should().Be("Grace Hopper");

        // And the obligation is discharged, so the sweep does not keep rediscovering it.
        _consumed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_banked_credit_reverses_the_tax_the_credited_period_was_charged()
    {
        var subscription = Subscribed();

        // A full year charged at 7.7% exclusive: 100_000 net, 7_700 tax, 107_700 for the period.
        // Half of it is being handed back.
        Owing(
            subscription,
            LegacyBankedCreditSource(
                subscription,
                "v4",
                creditedMinor: 53_850,
                settlement: new SubscriptionSettlementBreakdown
                {
                    Outgoing = new SubscriptionSettlementSide
                    {
                        GrossAmountMinor = 100_000,
                        TaxAmountMinor = 7_700,
                        PeriodTotalMinor = 107_700,
                        ProratedValueMinor = 53_850
                    },
                    NetSettlementMinor = -53_850
                },
                initiatedByUserId: null,
                occurredAtUtc: SettledAt,
                "corr-1")!);

        await Issuer().IssueForSubscriptionAsync(
            TenantId, SubscriptionId, "corr-1", CancellationToken.None);

        var document = _documents.Documents.Single();

        // Split in the proportion the charge itself was, so the subscriber can reverse the tax they
        // reclaimed. Reporting the whole 53_850 as untaxed net would leave them unable to.
        document.Amounts.NetSubtotalMinor.Should().Be(50_000);
        document.Amounts.TaxAmountMinor.Should().Be(3_850);
        document.Amounts.TotalMinor.Should().Be(53_850);

        // Net plus tax is the total, exactly. That is the invariant the largest-remainder allocation
        // exists to keep.
        (document.Amounts.NetSubtotalMinor + document.Amounts.TaxAmountMinor)
            .Should().Be(document.Amounts.TotalMinor);

        // The rate and mode of the plan being left, not the one being moved to: a change can cross
        // between inclusive and exclusive tax, and the credited period was charged at the old one.
        document.Amounts.TaxRateBasisPoints.Should().Be(770);
    }

    [Fact]
    public async Task A_banked_credit_links_the_invoice_that_charged_for_the_period_it_adjusts()
    {
        var subscription = Subscribed();
        SettledRenewal(payment => payment.OrderId =
            SubscriptionConstants.OrderIdFor(SubscriptionId));

        var issuer = Issuer();
        var invoice = await issuer.IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        Owing(
            subscription,
            LegacyBankedCreditSource(
                subscription,
                "v4",
                creditedMinor: 4_250,
                settlement: null,
                initiatedByUserId: null,
                occurredAtUtc: SettledAt,
                "corr-2")!);

        await issuer.IssueForSubscriptionAsync(
            TenantId, SubscriptionId, "corr-2", CancellationToken.None);

        var creditNote = _documents.Documents
            .Single(document => document.DocumentType == FinancialDocumentType.CreditNote);

        // Linked, so a subscriber holding two figures can see which charge the second one comes off.
        creditNote.OriginalDocumentId.Should().Be(invoice!.ItemId);
        creditNote.OriginalDocumentNumber.Should().Be(invoice.DocumentNumber);
    }

    [Fact]
    public async Task A_change_that_banks_nothing_gets_no_credit_note()
    {
        var subscription = Subscribed();

        // Refused at the source: there is nothing to record an obligation about.
        LegacyBankedCreditSource(
            subscription, "v4", 0, null, null, SettledAt, "corr-1")
            .Should().BeNull();

        (await Issuer().IssueForSubscriptionAsync(
            TenantId, SubscriptionId, "corr-1", CancellationToken.None))
            .Should().Be(0);
    }

    [Fact]
    public async Task A_renewal_names_no_person_because_none_acted()
    {
        Subscribed();
        SettledRenewal(payment => payment.UserId = null);

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        // Naming whoever last touched the subscription would attribute a charge to somebody who may
        // have left the company a year ago.
        document!.InitiatedBy.Name.Should().Be("System renewal");
        document.InitiatedBy.UserId.Should().BeNull();
    }

    [Fact]
    public async Task An_invoice_names_the_person_who_asked_for_the_change()
    {
        Subscribed();
        SettledRenewal(payment => payment.UserId = "user-7");

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        document!.InitiatedBy.UserId.Should().Be("user-7");
        document.InitiatedBy.Name.Should().Be("Grace Hopper");
        document.InitiatedBy.Email.Should().Be("grace@northwind.example");
    }

    [Fact]
    public async Task A_subscriber_with_no_profile_is_still_invoiced_by_their_organization_id()
    {
        _profiles
            .Setup(profiles => profiles.GetAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionBillingProfile?)null);

        Subscribed();
        SettledRenewal();

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        // The money has moved. Refusing to issue a document over a missing name would leave the
        // subscriber with no record of what they paid; the requirement is enforced before the charge.
        document.Should().NotBeNull();
        document!.Subscriber.LegalName.Should().Be(OrganizationId);
    }

    [Fact]
    public async Task Editing_a_billing_profile_does_not_change_an_issued_document()
    {
        Subscribed();
        SettledRenewal();

        var issuer = Issuer();
        var document = await issuer.IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        _profiles
            .Setup(profiles => profiles.GetAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionBillingProfile
            {
                LegalName = "Renamed Holdings SA",
                BillingContactName = "Someone Else",
                BillingContactEmail = "else@example.test"
            });

        // Re-reading the ledger, not re-issuing: the document is a snapshot and nothing re-reads the
        // profile on its behalf.
        var stored = await _documents.GetAsync(TenantId, document!.ItemId, CancellationToken.None);

        stored!.Subscriber.LegalName.Should().Be("Northwind Trading AG");
        stored.BillingContact.Name.Should().Be("Ada Byron");
    }

    [Fact]
    public async Task A_recovery_pass_issues_only_the_documents_the_money_path_missed()
    {
        Subscribed();
        SettledRenewal();

        _settledCharges
            .Setup(charges => charges.ListSettledSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubscriptionSettledChargeRecord(
                    "pay-1",
                    SubscriptionConstants.RenewalOrderIdFor(SubscriptionId, "M20260801T000000Z"),
                    SettledAt)
            ]);

        _settledCharges
            .Setup(charges => charges.ListRefundedSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var issuer = Issuer();

        (await issuer.IssuePendingAsync(TenantId, "sweep-1", CancellationToken.None))
            .Should().Be(1);

        // Second pass: the charge now has its document, so there is nothing to recover and nothing
        // is reported as recovered.
        (await issuer.IssuePendingAsync(TenantId, "sweep-2", CancellationToken.None))
            .Should().Be(0);
        _documents.Documents.Should().ContainSingle();
    }

    [Fact]
    public async Task An_invoice_issued_after_a_plan_change_still_describes_the_plan_that_was_charged()
    {
        var subscription = Subscribed();
        SettledRenewal();

        // The obligation the renewal recorded, naming the plan and the seats it charged for.
        Owing(
            subscription,
            SubscriptionDocumentSourceFactory.ForCharge(
                subscription,
                "pay-1",
                SubscriptionChargeKind.Renewal,
                "M20260801T000000Z",
                initiatedBy: null,
                occurredAtUtc: SettledAt,
                "corr-1"));

        // Then the subscriber moves on, before the document was ever written. This is the ordinary
        // case after an outage, not an exotic one.
        subscription.Plan = new PlanSnapshot { Code = "enterprise", DisplayName = "Enterprise" };
        subscription.Price = new PriceSnapshot
        {
            PriceId = "price-9",
            CurrencyCode = "CHF",
            UnitAmountMinor = 500_000,
            Interval = BillingInterval.Year,
            IntervalCount = 1
        };

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        // The plan that was charged for, not the one held now. Reading the live subscription would
        // put "Enterprise" and a 5000.00 unit price on an invoice for a 1000.00 Pro renewal.
        document!.Subject.PlanCode.Should().Be("pro");
        document.Subject.PlanName.Should().Be("Pro");
        document.Subject.PriceId.Should().Be("price-1");
        document.Subject.UnitAmountMinor.Should().Be(100_000);
        document.Lines.Should().ContainSingle().Which.Description.Should().Be("Pro");
    }

    [Fact]
    public async Task An_invoice_issued_after_a_seat_change_describes_the_seats_that_were_charged()
    {
        var subscription = Subscribed(item => item.QuantityItems =
        [
            new SubscriptionQuantityItem
            {
                ItemKey = "seats",
                UnitLabel = "Seats",
                Quantity = 3,
                UnitAmountMinor = 10_000
            }
        ]);

        SettledRenewal();

        Owing(
            subscription,
            SubscriptionDocumentSourceFactory.ForCharge(
                subscription,
                "pay-1",
                SubscriptionChargeKind.Renewal,
                "M20260801T000000Z",
                initiatedBy: null,
                occurredAtUtc: SettledAt,
                "corr-1"));

        subscription.QuantityItems[0].Quantity = 50;

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        var line = document!.Lines.Should().ContainSingle().Subject;
        line.Quantity.Should().Be(3);
        line.AmountMinor.Should().Be(30_000);
    }

    [Fact]
    public async Task A_flat_price_with_an_unpriced_capacity_item_prints_the_plan_unit_price()
    {
        // Capacity metadata is not a billed quantity. The production defect carried one user item
        // at zero because the price had no QuantityItemKey, then rendered that zero beside the
        // correctly charged flat subtotal.
        Subscribed(subscription => subscription.QuantityItems =
        [
            new SubscriptionQuantityItem
            {
                ItemKey = "lawyer-seat",
                UnitLabel = "Lawyer seat",
                Quantity = 1,
                UnitAmountMinor = 0
            }
        ]);
        SettledRenewal();

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        var line = document!.Lines.Should().ContainSingle().Subject;
        line.Description.Should().Be("Pro");
        line.ItemKey.Should().BeNull();
        line.Quantity.Should().Be(1);
        line.UnitAmountMinor.Should().Be(100_000);
        line.AmountMinor.Should().Be(document.Amounts.GrossSubtotalMinor);
    }

    [Fact]
    public async Task The_recorded_obligation_names_who_acted_even_if_they_are_since_unknown()
    {
        var subscription = Subscribed();
        SettledRenewal(payment => payment.UserId = null);

        Owing(
            subscription,
            SubscriptionDocumentSourceFactory.ForCharge(
                subscription,
                "pay-1",
                SubscriptionChargeKind.PlanChange,
                null,
                initiatedBy: SubscriptionDocumentSourceFactory.ActorOf(
                    "user-99",
                    "Katherine Johnson",
                    "katherine@northwind.example"),
                occurredAtUtc: SettledAt,
                "corr-1"));

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        // Their own name and address, captured when they acted. There is no contact recorded for
        // user-99, so without this the document would name them by their identifier — or worse, by the
        // organization's finance mailbox.
        document!.InitiatedBy.Name.Should().Be("Katherine Johnson");
        document.InitiatedBy.Email.Should().Be("katherine@northwind.example");
        document.InitiatedBy.UserId.Should().Be("user-99");
    }

    [Fact]
    public async Task A_refund_credit_note_says_it_was_the_system_refunding_not_the_system_renewing()
    {
        Subscribed();
        RefundedBy(959.30m, PaymentStatuses.Refunded, 959.30m);

        var issuer = Issuer();
        await issuer.IssueDocumentForPaymentAsync(TenantId, "pay-1", "corr-1", CancellationToken.None);

        var creditNote = await issuer.IssueRefundCreditNoteAsync(
            TenantId, "pay-1", "refund-1", "corr-2", CancellationToken.None);

        // "System renewal" is reserved for the clock renewing a subscription. A refund is not a
        // renewal, and a document should not say it was.
        creditNote!.InitiatedBy.Name.Should().Be("System refund");
    }

    [Fact]
    public async Task Recovery_reads_from_where_it_last_finished_rather_than_a_fixed_window()
    {
        Subscribed();
        SettledRenewal();

        var mark = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        _cursors
            .Setup(cursors => cursors.GetAsync(
                TenantId,
                SubscriptionFinancialDocumentIssuer.SettledChargeCursor,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialDocumentSweepMark(mark, null));

        DateTime? scannedFrom = null;
        _settledCharges
            .Setup(charges => charges.ListSettledSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateTime since, string? _, int _, CancellationToken _) =>
            {
                scannedFrom = since;

                return [new SubscriptionSettledChargeRecord("pay-1", null, SettledAt)];
            });

        _settledCharges
            .Setup(charges => charges.ListRefundedSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        (await Issuer().IssuePendingAsync(TenantId, "sweep-1", CancellationToken.None))
            .Should().Be(1);

        // Seven months back, because that is where the last pass stopped. A fixed lookback would have
        // scanned the last few hours and left this charge undocumented for good, with nothing saying so.
        scannedFrom.Should().Be(mark);

        // And the mark moves to the last record accounted for — the instant *and* which one it was, so
        // the next pass resumes at a position in a total order rather than at a moment several records
        // may share.
        _cursors.Verify(
            cursors => cursors.SetAsync(
                TenantId,
                SubscriptionFinancialDocumentIssuer.SettledChargeCursor,
                new FinancialDocumentSweepMark(SettledAt, "pay-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_full_batch_still_advances_the_mark_so_the_next_page_is_reached()
    {
        Subscribed();
        SettledRenewal();

        _settledCharges
            .Setup(charges => charges.ListSettledSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateTime _, string? _, int limit, CancellationToken _) =>
            [
                .. Enumerable.Range(0, limit)
                    .Select(index => new SubscriptionSettledChargeRecord(
                        $"pay-{index}", null, SettledAt))
            ]);

        _settledCharges
            .Setup(charges => charges.ListRefundedSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await Issuer().IssuePendingAsync(TenantId, "sweep-1", CancellationToken.None);

        // A full page is not a reason to hold the mark back. This used to skip the write on a full
        // batch, reasoning that a full batch meant more remained at that instant — which conflated a
        // tie on an instant with a page simply being full. The result was a livelock: a tenant with
        // more than one page of history re-read the same page forever and never reached anything after
        // it, every pass looking healthy and issuing nothing.
        //
        // The page is ordered and the mark names a position in that order, so resuming after the last
        // record read reaches the next one whether or not the page was full.
        _cursors.Verify(
            cursors => cursors.SetAsync(
                TenantId,
                SubscriptionFinancialDocumentIssuer.SettledChargeCursor,
                new FinancialDocumentSweepMark(SettledAt, "pay-24"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_pass_after_a_full_batch_reads_the_records_the_first_could_not_fit()
    {
        Subscribed();
        SettledRenewal();

        // Thirty charges settled in the same instant, read twenty-five at a time. Sharing an instant
        // is what makes this the hard case: paging on the instant alone cannot separate them.
        var all = Enumerable.Range(0, 30)
            .Select(index => new SubscriptionSettledChargeRecord(
                $"pay-{index:D2}", null, SettledAt))
            .ToList();

        FinancialDocumentSweepMark? stored = null;

        _cursors
            .Setup(cursors => cursors.SetAsync(
                TenantId,
                SubscriptionFinancialDocumentIssuer.SettledChargeCursor,
                It.IsAny<FinancialDocumentSweepMark>(),
                It.IsAny<CancellationToken>()))
            .Callback((string _, string _, FinancialDocumentSweepMark mark, CancellationToken _) =>
                stored = mark)
            .Returns(Task.CompletedTask);

        _cursors
            .Setup(cursors => cursors.GetAsync(
                TenantId,
                SubscriptionFinancialDocumentIssuer.SettledChargeCursor,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);

        // The keyset page the repository implements, standing in for the query.
        _settledCharges
            .Setup(charges => charges.ListSettledSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                string _,
                DateTime since,
                string? afterId,
                int limit,
                CancellationToken _) =>
            [
                .. all
                    .Where(charge => charge.SettledAtUtc > since ||
                        (charge.SettledAtUtc == since &&
                            (afterId is null ||
                                string.CompareOrdinal(charge.PaymentDetailId, afterId) > 0)))
                    .OrderBy(charge => charge.SettledAtUtc)
                    .ThenBy(charge => charge.PaymentDetailId, StringComparer.Ordinal)
                    .Take(limit)
            ]);

        _settledCharges
            .Setup(charges => charges.ListRefundedSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var issuer = Issuer();

        // A charge that needs no document, so this measures paging rather than issuing: every one is
        // read, and what matters is which ones the second pass sees.
        var firstPass = await issuer.IssuePendingAsync(TenantId, "sweep-1", CancellationToken.None);
        stored!.Value.AfterId.Should().Be("pay-24");

        await issuer.IssuePendingAsync(TenantId, "sweep-2", CancellationToken.None);

        // Past the twenty-five it could fit, on to the last five. Under the old rule the mark never
        // moved and those five were never reached.
        stored.Value.AfterId.Should().Be("pay-29");
        firstPass.Should().Be(0);
    }

    [Fact]
    public async Task A_trial_whose_obligation_was_lost_is_still_found_by_its_own_sweep()
    {
        var subscription = Subscribed(item => item.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            RequiresPaymentMethod = false
        });

        // Nothing owing on the subscription: the append was lost, or the trial predates obligations
        // being recorded at all. A trial takes no payment either, so the charge sweep cannot help.
        subscription.PendingDocumentSources.Should().BeEmpty();

        _subscriptions
            .Setup(subscriptions => subscriptions.ListTrialsStartedSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([subscription]);

        _settledCharges
            .Setup(charges => charges.ListSettledSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _settledCharges
            .Setup(charges => charges.ListRefundedSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        (await Issuer().IssuePendingAsync(TenantId, "sweep-1", CancellationToken.None))
            .Should().Be(1);

        _documents.Documents.Should().ContainSingle()
            .Which.DocumentType.Should().Be(FinancialDocumentType.TrialInvoice);
    }

    [Fact]
    public async Task An_obligation_of_any_age_is_recovered_because_the_sweep_has_no_window()
    {
        var subscription = Subscribed();

        // Recorded two years ago and never written. There is no time filter anywhere on this path:
        // the query is a test for a non-empty array, so age cannot put an obligation out of reach.
        Owing(
            subscription,
            LegacyBankedCreditSource(
                subscription,
                "v4",
                creditedMinor: 4_250,
                settlement: null,
                initiatedByUserId: null,
                occurredAtUtc: SettledAt.AddYears(-2),
                "corr-old")!);

        _subscriptions
            .Setup(subscriptions => subscriptions.ListWithPendingDocumentSourcesAsync(
                TenantId,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([subscription]);

        _settledCharges
            .Setup(charges => charges.ListSettledSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _settledCharges
            .Setup(charges => charges.ListRefundedSinceAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        (await Issuer().IssuePendingAsync(TenantId, "sweep-1", CancellationToken.None))
            .Should().Be(1);

        _documents.Documents.Should().ContainSingle()
            .Which.DocumentType.Should().Be(FinancialDocumentType.CreditNote);
    }

    [Fact]
    public async Task A_document_names_the_seller_this_tenant_issues_under()
    {
        Subscribed();
        SettledRenewal();

        _merchants
            .Setup(merchants => merchants.ResolveAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialDocumentMerchant
            {
                LegalName = "Northwind Software GmbH",
                TaxRegistrationId = "DE811234567"
            });

        var document = await Issuer().IssueDocumentForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        // This tenant's own seller, not one configured for the whole deployment. An invoice names a
        // seller in law, and every tenant issuing under one company would be a false statement on a
        // financial record rather than a presentation defect.
        document!.Merchant.LegalName.Should().Be("Northwind Software GmbH");
        document.Merchant.TaxRegistrationId.Should().Be("DE811234567");
    }

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
            NullLogger<SubscriptionFinancialDocumentIssuer>.Instance);

    /// <summary>
    /// Appends the obligation a transition would have left, so the issuer has terms to read.
    /// </summary>
    /// <remarks>
    /// The production path is the announcer or the compare-and-set that banked the credit. Here the
    /// source is put on the subscription directly, because what these tests are about is what the
    /// issuer does with one — not how it got there.
    /// </remarks>
    private void Owing(SubscriptionDetail subscription, SubscriptionDocumentSource source)
    {
        subscription.PendingDocumentSources.Add(source);
        _consumed[source.SourceKey] = source;
    }

    private SubscriptionDetail Subscribed(Action<SubscriptionDetail>? customize = null)
    {
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            CurrencyCode = "CHF",
            Status = SubscriptionStatus.Active,
            Plan = new PlanSnapshot { Code = "pro", DisplayName = "Pro" },
            Price = new PriceSnapshot
            {
                PriceId = "price-1",
                CurrencyCode = "CHF",
                UnitAmountMinor = 100_000,
                Interval = BillingInterval.Year,
                IntervalCount = 1,
                TaxRateBasisPoints = 770,
                TaxMode = TaxMode.Exclusive
            },
            FeeSchedule = new BillingSchedule
            {
                Interval = BillingInterval.Year,
                IntervalCount = 1,
                AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                TimeZoneId = "Europe/Zurich",
                AnchorDayOfMonth = 1
            },
            CurrentPeriodStartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEndUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        customize?.Invoke(subscription);

        _subscriptions
            .Setup(subscriptions => subscriptions.GetByIdAsync(
                TenantId,
                SubscriptionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        return subscription;
    }

    private PaymentDetail SettledRenewal(Action<PaymentDetail>? customize = null)
    {
        var payment = new PaymentDetail
        {
            ItemId = "pay-1",
            TenantId = TenantId,
            CustomerOrganizationId = OrganizationId,
            PaymentStatus = PaymentStatuses.Captured,
            PaymentFlow = PaymentFlows.SubscriptionInvoice,
            PaymentDate = SettledAt,
            CurrencyCode = "CHF",
            PreciseAmount = 959.30m,
            OrderId = SubscriptionConstants.RenewalOrderIdFor(
                SubscriptionId,
                "M20260801T000000Z"),
            UserId = "user-7",
            SubscriptionGrossAmountMinor = 100_000,
            SubscriptionBuiltInDiscountMinor = 8_000,
            SubscriptionPromotionalDiscountMinor = 2_000,
            SubscriptionNetAmountMinor = 90_000,
            SubscriptionTaxAmountMinor = 6_930,
            SubscriptionCreditAmountMinor = 1_000,
            SubscriptionTaxRateBasisPoints = 770,
            SubscriptionTaxMode = nameof(TaxMode.Exclusive),
            SubscriptionAutomaticDiscountBasisPoints = 800,
            SubscriptionQuantityDiscountBasisPoints = null,
            SubscriptionDiscountCombination = nameof(AutomaticDiscountCombination.BestDiscount)
        };

        customize?.Invoke(payment);

        _payments
            .Setup(payments => payments.GetByIdAsync(
                TenantId,
                payment.ItemId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        return payment;
    }

    /// <summary>
    /// A banked-credit obligation exactly as one was persisted before plan and quantity changes
    /// stopped banking credit.
    /// </summary>
    /// <remarks>
    /// Built here rather than by <see cref="SubscriptionDocumentSourceFactory"/>, which no longer
    /// has a method that produces one: nothing may write a new banked-credit source now. The
    /// issuer still has to <em>drain</em> the ones already on disk, though — a downgrade settled
    /// before that policy changed has a source waiting and a balance that already moved — so the
    /// shape is reproduced faithfully here to keep that path covered.
    /// <para>
    /// Returns null for a non-positive credit, mirroring what the factory refused to build, so the
    /// "banks nothing, records nothing" case still reads the same way.
    /// </para>
    /// </remarks>
    private static SubscriptionDocumentSource? LegacyBankedCreditSource(
        SubscriptionDetail subscription,
        string changeReference,
        long creditedMinor,
        SubscriptionSettlementBreakdown? settlement,
        string? initiatedByUserId,
        DateTime occurredAtUtc,
        string correlationId)
    {
        if (creditedMinor <= 0 || string.IsNullOrWhiteSpace(changeReference))
        {
            return null;
        }

        return new SubscriptionDocumentSource
        {
            SourceKey = FinancialDocumentSourceKey.ForDowngradeCredit(
                subscription.ItemId, changeReference),
            SettlementReservationId = changeReference,
            DocumentType = FinancialDocumentType.CreditNote,
            ChargeKind = SubscriptionChargeKind.PlanChange,
            CurrencyCode = subscription.CurrencyCode,
            Subject = SubscriptionDocumentSourceFactory.SubjectOf(subscription),
            QuantityItems =
            [
                .. subscription.QuantityItems.Select(item => new SubscriptionQuantityItem
                {
                    ItemKey = item.ItemKey,
                    UnitLabel = item.UnitLabel,
                    Quantity = item.Quantity,
                    UnitAmountMinor = item.UnitAmountMinor
                })
            ],
            Period = new FinancialDocumentPeriod
            {
                StartUtc = subscription.CurrentPeriodStartUtc,
                EndUtc = subscription.CurrentPeriodEndUtc,
                TimeZoneId = subscription.FeeSchedule.TimeZoneId
            },
            Settlement = settlement,
            CreditedMinor = creditedMinor,
            Amounts = FinancialDocumentCreditComposition.ForBankedCredit(
                subscription.Price, settlement, creditedMinor),
            Lines =
            [
                new FinancialDocumentLine
                {
                    Description = $"Unused time credited from {subscription.Plan.DisplayName}",
                    AmountMinor = creditedMinor
                }
            ],
            InitiatedBy = initiatedByUserId is { Length: > 0 } userId
                ? new FinancialDocumentPerson { UserId = userId }
                : null,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId
        };
    }

    private void RefundedBy(
        decimal amount,
        string paymentStatus,
        decimal refundedTotal,
        string refundStatus = PaymentRefundStatuses.Succeeded)
    {
        SettledRenewal(payment =>
        {
            payment.PaymentStatus = paymentStatus;
            payment.RefundedAmount = refundedTotal;
            payment.Refunds =
            [
                new PaymentRefund
                {
                    RefundId = "refund-1",
                    Status = refundStatus,
                    Amount = amount,
                    CurrencyCode = "CHF",
                    CompletedAtUtc = SettledAt.AddDays(3)
                }
            ];
        });
    }
}
