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
    private readonly Mock<ICurrencyMinorUnitResolver> _currency = new();

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

        var document = await Issuer().IssueForPaymentAsync(
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

        var document = await Issuer().IssueForPaymentAsync(
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

        var document = await Issuer().IssueForPaymentAsync(
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
        var first = await issuer.IssueForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);
        var second = await issuer.IssueForPaymentAsync(
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
            payment.SubscriptionSettlement = new SubscriptionSettlementBreakdown
            {
                Outgoing = new SubscriptionSettlementSide { ProratedValueMinor = 3_000 },
                Target = new SubscriptionSettlementSide { ProratedValueMinor = 9_000 },
                CreditConsumedMinor = 0,
                NetSettlementMinor = 6_000
            };
        });

        var document = await Issuer().IssueForPaymentAsync(
            TenantId, "pay-1", "corr-1", CancellationToken.None);

        // A settlement is a subtraction between two prorated periods, and a single subtotal cannot
        // explain one. The invoice has to carry both sides or the subscriber cannot check it.
        document!.Settlement.Should().NotBeNull();
        document.Settlement!.NetSettlementMinor.Should().Be(6_000);
        document.Lines.Should().ContainSingle();
    }

    [Theory]
    [InlineData(PaymentStatuses.Refused)]
    [InlineData(PaymentStatuses.Processing)]
    [InlineData(PaymentStatuses.Authorized)]
    [InlineData(PaymentStatuses.Cancelled)]
    public async Task A_charge_that_never_settled_produces_no_document(string status)
    {
        Subscribed();
        SettledRenewal(payment => payment.PaymentStatus = status);

        var document = await Issuer().IssueForPaymentAsync(
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

        var document = await Issuer().IssueForPaymentAsync(
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

        var issuer = Issuer();
        var document = await issuer.IssueTrialInvoiceAsync(
            subscription, "corr-1", CancellationToken.None);

        document.Should().NotBeNull();
        document!.DocumentType.Should().Be(FinancialDocumentType.TrialInvoice);
        document.Amounts.TotalMinor.Should().Be(0);
        document.Amounts.NetSubtotalMinor.Should().Be(0);
        document.Trial!.RequiresPaymentMethod.Should().BeFalse();
        document.Trial.StartsAtUtc.Should().Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        // Numbered in the invoice series, not a third one: a subscriber whose first document is
        // INV-2026-000001 can tell they have all of them.
        document.DocumentNumber.Should().StartWith("INV-");

        // And exactly one, however many times the trial is announced.
        await issuer.IssueTrialInvoiceAsync(subscription, "corr-2", CancellationToken.None);
        _documents.Documents.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_subscription_with_no_trial_gets_no_trial_invoice()
    {
        var subscription = Subscribed();

        var document = await Issuer().IssueTrialInvoiceAsync(
            subscription, "corr-1", CancellationToken.None);

        document.Should().BeNull();
    }

    [Fact]
    public async Task A_full_refund_reverses_exactly_what_was_charged_and_links_to_its_invoice()
    {
        Subscribed();
        SettledRenewal();

        var issuer = Issuer();
        var invoice = await issuer.IssueForPaymentAsync(
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
        var invoice = await issuer.IssueForPaymentAsync(
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

        var document = await Issuer().IssueDowngradeCreditNoteAsync(
            subscription,
            "v4",
            creditedMinor: 4_250,
            settlement: new SubscriptionSettlementBreakdown { NetSettlementMinor = -4_250 },
            initiatedByUserId: "user-7",
            "corr-1",
            CancellationToken.None);

        document.Should().NotBeNull();
        document!.DocumentType.Should().Be(FinancialDocumentType.CreditNote);
        document.Amounts.TotalMinor.Should().Be(4_250);
        document.SettlementReservationId.Should().Be("v4");

        // Named, because a person asked for the downgrade. A renewal would say "System renewal".
        document.InitiatedBy.Name.Should().Be("Grace Hopper");
    }

    [Fact]
    public async Task A_change_that_banks_nothing_gets_no_credit_note()
    {
        var subscription = Subscribed();

        var document = await Issuer().IssueDowngradeCreditNoteAsync(
            subscription, "v4", 0, null, null, "corr-1", CancellationToken.None);

        document.Should().BeNull();
    }

    [Fact]
    public async Task A_renewal_names_no_person_because_none_acted()
    {
        Subscribed();
        SettledRenewal(payment => payment.UserId = null);

        var document = await Issuer().IssueForPaymentAsync(
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

        var document = await Issuer().IssueForPaymentAsync(
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

        var document = await Issuer().IssueForPaymentAsync(
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
        var document = await issuer.IssueForPaymentAsync(
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

    private ISubscriptionFinancialDocumentIssuer Issuer() =>
        new SubscriptionFinancialDocumentIssuer(
            _documents,
            _numbers.Object,
            _profiles.Object,
            _subscriptions.Object,
            _payments.Object,
            _settledCharges.Object,
            _currency.Object,
            Options.Create(new SubscriptionOptions
            {
                Invoicing = new SubscriptionInvoicingOptions { LegalName = "Blocks AG" }
            }),
            NullLogger<SubscriptionFinancialDocumentIssuer>.Instance);

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
