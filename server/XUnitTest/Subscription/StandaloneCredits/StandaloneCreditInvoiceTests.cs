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
using XUnitTest.Subscription.Credits;

namespace XUnitTest.Subscription.StandaloneCredits;

/// <summary>
/// The invoice a standalone credit purchase produces (spec 005, AC-3).
/// </summary>
/// <remarks>
/// Guards against an organization paying for credits and getting no document, a document that
/// states figures other than the ones the purchase was quoted at, one issued twice for the same
/// payment, or one issued before the money has actually settled.
/// </remarks>
public sealed class StandaloneCreditInvoiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string PaymentId = "pay-1";

    private static readonly DateTime SettledAt = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    private readonly FinancialDocumentLedgerFake _documents = new();
    private readonly Mock<IFinancialDocumentNumberAllocator> _numbers = new();
    private readonly Mock<ISubscriptionBillingProfileRepository> _profiles = new();
    private readonly Mock<ISubscriptionMerchantProfileService> _merchants = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly InMemoryCreditPurchases _purchases = new();

    private string _paymentStatus = PaymentStatuses.Captured;

    public StandaloneCreditInvoiceTests()
    {
        _numbers
            .Setup(numbers => numbers.AllocateAsync(TenantId, FinancialDocumentType.Invoice, 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync("INV-2026-000001");
        _profiles
            .Setup(profiles => profiles.GetAsync(TenantId, OrganizationId, It.IsAny<CancellationToken>()))
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
        _payments
            .Setup(payments => payments.GetByIdAsync(TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PaymentDetail
            {
                ItemId = PaymentId,
                TenantId = TenantId,
                PaymentStatus = _paymentStatus,
                PaymentDate = SettledAt,
                CurrencyCode = "USD"
            });

        _purchases.All.Add(new SubscriptionCreditPurchase
        {
            ItemId = "org-1:k1",
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            SubscriptionId = null,
            MeterKey = "ai",
            MeterDisplayName = "AI",
            Quantity = 100,
            CurrencyCode = "USD",
            UnitAmountMinor = 10,
            GrossMinor = 1_000,
            DiscountMinor = 200,
            NetMinor = 800,
            TaxMinor = 65,
            TotalMinor = 865,
            TaxRateBasisPoints = 810,
            TaxMode = TaxMode.Exclusive,
            DiscountCode = "topup20",
            PaymentId = PaymentId,
            Status = CreditPurchaseStatus.Finalized
        });
    }

    [Fact]
    public async Task A_settled_standalone_purchase_is_invoiced_from_its_own_record()
    {
        var result = await Issuer().IssueForCreditPurchaseAsync(TenantId, "org-1:k1", "corr-1", CancellationToken.None);

        result.Outcome.Should().Be(FinancialDocumentIssueOutcome.Issued, "AC-3");
        var document = _documents.Documents.Should().ContainSingle().Subject;
        document.SubscriptionId.Should().BeEmpty("the invoice belongs to the organization, not to a subscription");
        document.OrganizationId.Should().Be(OrganizationId);
        document.PaymentDetailId.Should().Be(PaymentId);
        document.SourceKey.Should().Be(FinancialDocumentSourceKey.ForPayment(PaymentId));
        document.Subscriber.LegalName.Should().Be("Northwind Trading AG");
        document.Subject.PlanName.Should().Be("AI credits");
        document.Lines.Should().ContainSingle().Which.Quantity.Should().Be(100);
        document.Amounts.GrossSubtotalMinor.Should().Be(1_000);
        document.Amounts.PromotionalDiscountMinor.Should().Be(200);
        document.Amounts.NetSubtotalMinor.Should().Be(800);
        document.Amounts.TaxAmountMinor.Should().Be(65);
        document.Amounts.TotalMinor.Should().Be(865, "the invoice states exactly what was quoted and charged");
        document.Amounts.PromotionCode.Should().Be("topup20");
        document.Period.StartUtc.Should().Be(default, "credits bought outright cover no period");
    }

    [Fact]
    public async Task Issuing_twice_for_one_payment_returns_the_first_document()
    {
        var issuer = Issuer();
        await issuer.IssueForCreditPurchaseAsync(TenantId, "org-1:k1", "corr-1", CancellationToken.None);

        var again = await issuer.IssueForCreditPurchaseAsync(TenantId, "org-1:k1", "corr-2", CancellationToken.None);

        again.Outcome.Should().Be(FinancialDocumentIssueOutcome.AlreadyExists);
        _documents.Documents.Should().ContainSingle("a retried queue item must not number a second invoice");
    }

    [Fact]
    public async Task An_unsettled_payment_is_reported_as_not_settled_so_the_item_is_retried()
    {
        _paymentStatus = PaymentStatuses.Processing;

        var result = await Issuer().IssueForCreditPurchaseAsync(TenantId, "org-1:k1", "corr-1", CancellationToken.None);

        result.Outcome.Should().Be(FinancialDocumentIssueOutcome.PaymentNotSettled);
        _documents.Documents.Should().BeEmpty("revenue the bank never saw must not enter the ledger");
    }

    [Fact]
    public async Task A_subscription_purchase_or_an_unknown_id_is_not_this_path()
    {
        _purchases.All[0].SubscriptionId = "sub-1";

        var subscriptionOwned = await Issuer().IssueForCreditPurchaseAsync(TenantId, "org-1:k1", "corr-1", CancellationToken.None);
        var unknown = await Issuer().IssueForCreditPurchaseAsync(TenantId, "nope", "corr-2", CancellationToken.None);

        subscriptionOwned.Outcome.Should().Be(FinancialDocumentIssueOutcome.UnknownCharge,
            "a subscription purchase is invoiced through its subscription, as spec 002 says");
        unknown.Outcome.Should().Be(FinancialDocumentIssueOutcome.UnknownCharge);
    }

    private SubscriptionFinancialDocumentIssuer Issuer() => new(
        _documents,
        _numbers.Object,
        _profiles.Object,
        _merchants.Object,
        Mock.Of<ISubscriptionRepository>(),
        _payments.Object,
        Mock.Of<ISubscriptionInvoiceHistoryRepository>(),
        Mock.Of<ISubscriptionDocumentCursorRepository>(),
        Mock.Of<ICurrencyMinorUnitResolver>(),
        Options.Create(new SubscriptionOptions()),
        NullLogger<SubscriptionFinancialDocumentIssuer>.Instance,
        creditPurchases: _purchases);
}
