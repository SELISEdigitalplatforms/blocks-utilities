using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

public sealed class SubscriptionInvoiceHistoryServiceTests
{
    private readonly Mock<ISubscriptionContextResolver> _context = new();
    private readonly Mock<ISubscriptionInvoiceHistoryRepository> _invoices = new();

    public SubscriptionInvoiceHistoryServiceTests()
    {
        _context
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(
                    "tenant-1",
                    "subscriber-1",
                    "actor-1",
                    "user-1")));
    }

    [Fact]
    public async Task History_maps_invoice_metadata_download_links_and_next_cursor()
    {
        _invoices
            .Setup(repository => repository.ListAsync(
                "tenant-1",
                "subscriber-1",
                2,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionInvoiceHistoryPage(
                [Invoice("payment/2", "sub:subscription-2:2026-08"),
                 Invoice("payment-1", "legacy-order")],
                true));

        var result = await Service().ListAsync(
            new GetSubscriptionInvoicesRequest { PageSize = 2 },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value.Items[0].SubscriptionId.Should().Be("subscription-2");
        result.Value.Items[0].InvoiceType.Should().Be("Renewal");
        result.Value.Items[0].PeriodKey.Should().Be("2026-08");
        result.Value.Items[0].DownloadUrl.Should().Be(
            "/api/subscriptions/invoices/payment%2F2/pdf");
        result.Value.Items[1].SubscriptionId.Should().BeNull();
        result.Value.Items[1].InvoiceType.Should().Be("Unknown");
        result.Value.PageInfo.HasNextPage.Should().BeTrue();
        result.Value.PageInfo.NextCursor.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("sub:subscription-1:planchange:7", "PlanChange", null)]
    [InlineData("sub:subscription-1:usage:2026-08", "Usage", "2026-08")]
    public async Task Non_renewal_invoice_type_is_classified(
        string orderId,
        string expectedType,
        string? expectedPeriod)
    {
        _invoices
            .Setup(repository => repository.ListAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<SubscriptionInvoiceHistoryCursor?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionInvoiceHistoryPage(
                [Invoice("payment-1", orderId)],
                false));

        var result = await Service().ListAsync(
            new GetSubscriptionInvoicesRequest(),
            "corr-1",
            CancellationToken.None);

        result.Value!.Items[0].InvoiceType.Should().Be(expectedType);
        result.Value.Items[0].PeriodKey.Should().Be(expectedPeriod);
    }

    [Fact]
    public async Task Cursor_is_bound_to_the_resolved_organization()
    {
        var record = Invoice("payment-1", "sub:subscription-1:2026-07");
        var cursor = SubscriptionInvoiceHistoryCursorCodec.Encode("subscriber-1", record);
        _invoices
            .Setup(repository => repository.ListAsync(
                "tenant-1",
                "subscriber-1",
                25,
                It.Is<SubscriptionInvoiceHistoryCursor>(boundary =>
                    boundary.PaymentDetailId == "payment-1" &&
                    boundary.IssuedAtUtc == record.IssuedAtUtc),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionInvoiceHistoryPage([], false));

        var result = await Service().ListAsync(
            new GetSubscriptionInvoicesRequest { After = cursor },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        SubscriptionInvoiceHistoryCursorCodec.TryDecode(
            cursor,
            "another-organization",
            out _).Should().BeFalse();
    }

    [Fact]
    public async Task Console_organization_is_carried_into_download_links()
    {
        _invoices
            .Setup(repository => repository.ListAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<SubscriptionInvoiceHistoryCursor?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionInvoiceHistoryPage(
                [Invoice("payment-1", "sub:subscription-1:2026-08")],
                false));

        var result = await Service().ListAsync(
            new GetSubscriptionInvoicesRequest
            {
                OrganizationId = "subscriber-1"
            },
            "corr-1",
            CancellationToken.None);

        result.Value!.Items[0].DownloadUrl.Should().Be(
            "/api/subscriptions/invoices/payment-1/pdf?organizationId=subscriber-1");
        _context.Verify(resolver => resolver.ResolveAsync(
            "corr-1",
            "subscriber-1",
            It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Invalid_page_size_is_rejected_without_querying(int pageSize)
    {
        var result = await Service().ListAsync(
            new GetSubscriptionInvoicesRequest { PageSize = pageSize },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_invoice_query_invalid");
        _invoices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Invalid_cursor_is_rejected_without_querying()
    {
        var result = await Service().ListAsync(
            new GetSubscriptionInvoicesRequest { After = "not-a-cursor" },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ValidationErrors.Should().ContainKey("After");
        _invoices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_settlement_invoice_reports_both_sides_and_its_derived_taxable_amounts()
    {
        // The promise this keeps: a plan-change charge is a subtraction between two prorated periods,
        // and the invoice has to be able to show the subtraction rather than only its answer.
        var settled = Invoice("payment-3", "sub:subscription-3:settle:res-1") with
        {
            Settlement = new SubscriptionSettlementBreakdown
            {
                Outgoing = new SubscriptionSettlementSide
                {
                    GrossAmountMinor = 1_000,
                    BuiltInDiscountMinor = 100,
                    PromotionalDiscountMinor = 50,
                    TaxAmountMinor = 85,
                    PeriodTotalMinor = 935,
                    ProratedValueMinor = 467
                },
                Target = new SubscriptionSettlementSide
                {
                    GrossAmountMinor = 2_000,
                    BuiltInDiscountMinor = 160,
                    TaxAmountMinor = 184,
                    PeriodTotalMinor = 2_024,
                    ProratedValueMinor = 1_012
                },
                CreditConsumedMinor = 100,
                NetSettlementMinor = 445
            }
        };

        _invoices
            .Setup(repository => repository.ListAsync(
                "tenant-1", "subscriber-1", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionInvoiceHistoryPage([settled], false));

        var result = await Service().ListAsync(
            new GetSubscriptionInvoicesRequest(), "corr-1", CancellationToken.None);

        var invoice = result.Value!.Items[0];

        invoice.Settlement.Should().NotBeNull();
        invoice.Settlement!.Outgoing.DiscountedAmountMinor.Should().Be(
            850, "1,000 less the 100 built in and the 50 promotional");
        invoice.Settlement.Outgoing.ProratedValueMinor.Should().Be(467);
        invoice.Settlement.Target.DiscountedAmountMinor.Should().Be(1_840);
        invoice.Settlement.Target.PeriodTotalMinor.Should().Be(2_024);
        invoice.Settlement.CreditConsumedMinor.Should().Be(100);
        invoice.Settlement.NetSettlementMinor.Should().Be(445);

        // The renewal-shaped fields stay absent: this charge has two sides, not one.
        invoice.GrossAmountMinor.Should().BeNull();
    }

    [Fact]
    public async Task A_renewal_invoice_reports_its_own_breakdown_and_no_settlement()
    {
        var renewal = Invoice("payment-4", "sub:subscription-4:2026-09") with
        {
            GrossAmountMinor = 10_000,
            BuiltInDiscountMinor = 800,
            PromotionalDiscountMinor = 0,
            AutomaticDiscountBasisPoints = 800,
            DiscountCombination = "Additive"
        };

        _invoices
            .Setup(repository => repository.ListAsync(
                "tenant-1", "subscriber-1", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionInvoiceHistoryPage([renewal], false));

        var result = await Service().ListAsync(
            new GetSubscriptionInvoicesRequest(), "corr-1", CancellationToken.None);

        var invoice = result.Value!.Items[0];

        invoice.Settlement.Should().BeNull("a renewal is one priced period, not two");
        invoice.GrossAmountMinor.Should().Be(10_000);
        invoice.DiscountedAmountMinor.Should().Be(9_200);
        invoice.QuantityDiscountCombination.Should().Be("Additive");
    }

    private SubscriptionInvoiceHistoryService Service() => new(
        _context.Object,
        _invoices.Object);

    private static SubscriptionInvoiceHistoryRecord Invoice(
        string paymentId,
        string orderId) => new(
        paymentId,
        "STRIPE",
        orderId,
        "Claude Pro renewal",
        20m,
        0m,
        "USD",
        "CAPTURED",
        new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc));
}
