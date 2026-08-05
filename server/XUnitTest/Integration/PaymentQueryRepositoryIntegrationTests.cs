using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// Exercises the cursor, filter and sort translation of
/// <see cref="PaymentQueryRepository"/> against a real MongoDB. The filters and
/// sorts are expression trees the driver has to translate, so an in-memory
/// substitute would prove nothing about them.
/// </summary>
[Collection(MongoIntegrationCollection.Name)]
public sealed class PaymentQueryRepositoryIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly Mock<IPaymentRepository> _paymentRepository = new();
    private readonly PaymentQueryRepository _repository;

    /// <summary>
    /// The fixture hands every test class the same database, so document ids
    /// have to be unique per test instance. The prefix is constant within a
    /// test, which keeps the id tie-break ordering the cursors rely on intact.
    /// </summary>
    private readonly string _prefix = Guid.NewGuid().ToString("N")[..8] + "-";

    private string Id(string label) => _prefix + label;

    public PaymentQueryRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _repository = new PaymentQueryRepository(
            fixture.DbContextProvider,
            _paymentRepository.Object);
    }

    private static PaymentQueryCriteria Criteria(string tenantId) => new()
    {
        TenantId = tenantId,
        PageSize = 10,
        SortBy = PaymentQuerySortFields.PaymentDate,
        SortDirection = PaymentQuerySortDirections.Descending
    };

    private async Task<string> SeedAsync(params PaymentDetail[] payments)
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        foreach (var payment in payments)
        {
            payment.TenantId = tenantId;
        }

        await _fixture.Collection<PaymentDetail>("PaymentDetails")
            .InsertManyAsync(payments);

        return tenantId;
    }

    private PaymentDetail Payment(
        string id,
        string providerName = "adyen",
        decimal amount = 100m,
        string currencyCode = "CHF",
        string status = PaymentStatuses.Authorized,
        DateTime? paymentDate = null,
        string? orderId = null,
        string paymentFlow = PaymentFlows.HostedCheckout,
        List<PaymentRefund>? refunds = null) => new()
        {
            ItemId = Id(id),
            // The collection carries a unique tenant plus idempotency-key index,
            // so seeded payments need distinct keys.
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            ProviderName = providerName,
            PreciseAmount = amount,
            CurrencyCode = currencyCode,
            PaymentStatus = status,
            PaymentDate = paymentDate ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            OrderId = orderId,
            PaymentFlow = paymentFlow,
            Refunds = refunds ?? []
        };

    [Fact]
    public async Task Query_ensures_indexes_before_reading()
    {
        var tenantId = await SeedAsync(Payment("p1"));

        await _repository.QueryAsync(
            Criteria(tenantId),
            CancellationToken.None);

        _paymentRepository.Verify(
            x => x.EnsureIndexesAsync(tenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Query_only_returns_the_calling_tenants_payments()
    {
        var otherTenant = await SeedAsync(Payment("other-1"));
        var tenantId = await SeedAsync(Payment("mine-1"));

        var page = await _repository.QueryAsync(
            Criteria(tenantId),
            CancellationToken.None);

        page.Items.Should().ContainSingle();
        page.Items[0].PaymentDetailId.Should().Be(Id("mine-1"));
        otherTenant.Should().NotBe(tenantId);
    }

    [Fact]
    public async Task An_empty_tenant_is_refused_rather_than_read_across_tenants()
    {
        var act = () => _repository.QueryAsync(
            Criteria(" ") with { TenantId = " " },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Tenant context is required*");
    }

    [Fact]
    public async Task A_full_page_reports_more_and_does_not_leak_the_probe_row()
    {
        var tenantId = await SeedAsync(
            Payment("p1", paymentDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Payment("p2", paymentDate: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            Payment("p3", paymentDate: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with { PageSize = 2 },
            CancellationToken.None);

        page.HasMoreInQueryDirection.Should().BeTrue();
        page.Items.Should().HaveCount(2);
        page.Items.Select(item => item.PaymentDetailId)
            .Should().Equal(Id("p3"), Id("p2"));
    }

    [Fact]
    public async Task A_partial_page_reports_no_more()
    {
        var tenantId = await SeedAsync(Payment("p1"), Payment("p2"));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with { PageSize = 5 },
            CancellationToken.None);

        page.HasMoreInQueryDirection.Should().BeFalse();
        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_empty_collection_yields_an_empty_page()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var page = await _repository.QueryAsync(
            Criteria(tenantId),
            CancellationToken.None);

        page.Items.Should().BeEmpty();
        page.HasMoreInQueryDirection.Should().BeFalse();
    }

    [Fact]
    public async Task A_pending_refund_is_flagged_on_the_record()
    {
        var tenantId = await SeedAsync(
            Payment(
                "pending",
                refunds:
                [
                    new PaymentRefund
                    {
                        Status = PaymentRefundStatuses.Submitted,
                        IdempotencyKey = Guid.NewGuid().ToString("N")
                    }
                ]),
            Payment(
                "settled",
                paymentDate: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                refunds:
                [
                    new PaymentRefund
                    {
                        Status = PaymentRefundStatuses.Succeeded,
                        IdempotencyKey = Guid.NewGuid().ToString("N")
                    }
                ]));

        var page = await _repository.QueryAsync(
            Criteria(tenantId),
            CancellationToken.None);

        page.Items.Single(item => item.PaymentDetailId == Id("pending"))
            .HasPendingRefund.Should().BeTrue();
        page.Items.Single(item => item.PaymentDetailId == Id("settled"))
            .HasPendingRefund.Should().BeFalse();
    }

    [Theory]
    [InlineData(PaymentRefundStatuses.Initiating)]
    [InlineData(PaymentRefundStatuses.InitiationUnknown)]
    [InlineData(PaymentRefundStatuses.Submitted)]
    [InlineData(PaymentRefundStatuses.RequiresAttention)]
    public async Task Every_in_flight_refund_status_counts_as_pending(string status)
    {
        var tenantId = await SeedAsync(
            Payment(
                "p1",
                refunds:
                [
                    new PaymentRefund
                    {
                        Status = status,
                        IdempotencyKey = Guid.NewGuid().ToString("N")
                    }
                ]));

        var page = await _repository.QueryAsync(
            Criteria(tenantId),
            CancellationToken.None);

        page.Items.Single().HasPendingRefund.Should().BeTrue();
    }

    [Fact]
    public async Task Provider_and_status_filters_narrow_the_result()
    {
        var tenantId = await SeedAsync(
            Payment("adyen-auth", "adyen", status: PaymentStatuses.Authorized),
            Payment("stripe-auth", "stripe", status: PaymentStatuses.Authorized),
            Payment("adyen-failed", "adyen", status: PaymentStatuses.Refused));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                ProviderNames = ["adyen"],
                PaymentStatuses = [PaymentStatuses.Authorized]
            },
            CancellationToken.None);

        page.Items.Select(item => item.PaymentDetailId)
            .Should().Equal(Id("adyen-auth"));
    }

    [Fact]
    public async Task Amount_bounds_are_inclusive_on_both_ends()
    {
        var tenantId = await SeedAsync(
            Payment("low", amount: 5m),
            Payment("min", amount: 10m),
            Payment("max", amount: 20m),
            Payment("high", amount: 25m));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with { MinAmount = 10m, MaxAmount = 20m },
            CancellationToken.None);

        page.Items.Select(item => item.PaymentDetailId)
            .Should().BeEquivalentTo(Id("min"), Id("max"));
    }

    [Fact]
    public async Task The_payment_date_window_includes_the_start_and_excludes_the_end()
    {
        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        var tenantId = await SeedAsync(
            Payment("before", paymentDate: from.AddDays(-1)),
            Payment("start", paymentDate: from),
            Payment("inside", paymentDate: from.AddDays(1)),
            Payment("end", paymentDate: to));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                PaymentDateFromUtc = from,
                PaymentDateToUtc = to
            },
            CancellationToken.None);

        page.Items.Select(item => item.PaymentDetailId)
            .Should().BeEquivalentTo(Id("start"), Id("inside"));
    }

    [Fact]
    public async Task The_optional_equality_filters_are_all_applied()
    {
        var tenantId = await SeedAsync(
            Payment(
                "wanted",
                currencyCode: "EUR",
                orderId: "order-1",
                paymentFlow: PaymentFlows.HostedCheckout),
            Payment("wrong-currency", currencyCode: "CHF", orderId: "order-1"),
            Payment("wrong-order", currencyCode: "EUR", orderId: "order-2"));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                CurrencyCode = "EUR",
                OrderId = "order-1",
                PaymentDetailId = Id("wanted"),
                PaymentFlow = PaymentFlows.HostedCheckout
            },
            CancellationToken.None);

        page.Items.Select(item => item.PaymentDetailId)
            .Should().Equal(Id("wanted"));
    }

    [Theory]
    [InlineData(PaymentQuerySortFields.ProviderName, PaymentQuerySortDirections.Ascending)]
    [InlineData(PaymentQuerySortFields.ProviderName, PaymentQuerySortDirections.Descending)]
    [InlineData(PaymentQuerySortFields.Amount, PaymentQuerySortDirections.Ascending)]
    [InlineData(PaymentQuerySortFields.Amount, PaymentQuerySortDirections.Descending)]
    [InlineData(PaymentQuerySortFields.PaymentDate, PaymentQuerySortDirections.Ascending)]
    [InlineData(PaymentQuerySortFields.PaymentDate, PaymentQuerySortDirections.Descending)]
    [InlineData(PaymentQuerySortFields.PaymentStatus, PaymentQuerySortDirections.Ascending)]
    [InlineData(PaymentQuerySortFields.PaymentStatus, PaymentQuerySortDirections.Descending)]
    public async Task Every_sort_field_and_direction_is_translatable(
        string sortBy,
        string sortDirection)
    {
        var tenantId = await SeedAsync(
            Payment(
                "a",
                "adyen",
                10m,
                status: PaymentStatuses.Authorized,
                paymentDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Payment(
                "b",
                "stripe",
                20m,
                status: PaymentStatuses.Refused,
                paymentDate: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                SortBy = sortBy,
                SortDirection = sortDirection
            },
            CancellationToken.None);

        var expectedFirst = Id(
            sortDirection == PaymentQuerySortDirections.Ascending ? "a" : "b");
        page.Items.Select(item => item.PaymentDetailId)
            .First().Should().Be(expectedFirst);
    }

    [Fact]
    public async Task An_unsupported_sort_field_is_rejected()
    {
        var tenantId = await SeedAsync(Payment("p1"));

        var act = () => _repository.QueryAsync(
            Criteria(tenantId) with { SortBy = "shoeSize" },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task An_unsupported_sort_field_is_rejected_while_building_a_cursor()
    {
        var tenantId = await SeedAsync(Payment("p1"));

        var act = () => _repository.QueryAsync(
            Criteria(tenantId) with
            {
                SortBy = "shoeSize",
                CursorBoundary = new PaymentQueryCursorBoundary(
                    Id("p1"),
                    "adyen",
                    null,
                    null)
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Paging_forward_on_amount_resumes_after_the_cursor()
    {
        var tenantId = await SeedAsync(
            Payment("a", amount: 10m),
            Payment("b", amount: 20m),
            Payment("c", amount: 30m));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                SortBy = PaymentQuerySortFields.Amount,
                SortDirection = PaymentQuerySortDirections.Ascending,
                CursorBoundary = new PaymentQueryCursorBoundary(Id("a"), null, 10m, null)
            },
            CancellationToken.None);

        page.Items.Select(item => item.PaymentDetailId)
            .Should().Equal(Id("b"), Id("c"));
    }

    [Fact]
    public async Task Paging_backward_returns_the_rows_before_the_cursor_in_request_order()
    {
        var tenantId = await SeedAsync(
            Payment("a", amount: 10m),
            Payment("b", amount: 20m),
            Payment("c", amount: 30m));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                SortBy = PaymentQuerySortFields.Amount,
                SortDirection = PaymentQuerySortDirections.Ascending,
                IsBackward = true,
                CursorBoundary = new PaymentQueryCursorBoundary(Id("c"), null, 30m, null)
            },
            CancellationToken.None);

        // Read descending from the cursor, then reversed so the caller always
        // sees the page in the direction it asked for.
        page.Items.Select(item => item.PaymentDetailId)
            .Should().Equal(Id("a"), Id("b"));
    }

    [Fact]
    public async Task A_descending_forward_cursor_walks_downwards()
    {
        var tenantId = await SeedAsync(
            Payment("a", amount: 10m),
            Payment("b", amount: 20m),
            Payment("c", amount: 30m));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                SortBy = PaymentQuerySortFields.Amount,
                SortDirection = PaymentQuerySortDirections.Descending,
                CursorBoundary = new PaymentQueryCursorBoundary(Id("c"), null, 30m, null)
            },
            CancellationToken.None);

        page.Items.Select(item => item.PaymentDetailId)
            .Should().Equal(Id("b"), Id("a"));
    }

    [Fact]
    public async Task A_cursor_on_provider_name_breaks_ties_on_the_id()
    {
        var tenantId = await SeedAsync(
            Payment("a", "adyen"),
            Payment("b", "adyen"),
            Payment("c", "stripe"));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                SortBy = PaymentQuerySortFields.ProviderName,
                SortDirection = PaymentQuerySortDirections.Ascending,
                CursorBoundary = new PaymentQueryCursorBoundary(Id("a"), "adyen", null, null)
            },
            CancellationToken.None);

        page.Items.Select(item => item.PaymentDetailId)
            .Should().Equal(Id("b"), Id("c"));
    }

    [Fact]
    public async Task A_cursor_on_payment_date_resumes_after_the_boundary()
    {
        var first = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var tenantId = await SeedAsync(
            Payment("a", paymentDate: first),
            Payment("b", paymentDate: first.AddDays(1)),
            Payment("c", paymentDate: first.AddDays(2)));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                SortBy = PaymentQuerySortFields.PaymentDate,
                SortDirection = PaymentQuerySortDirections.Ascending,
                CursorBoundary = new PaymentQueryCursorBoundary(Id("a"), null, null, first)
            },
            CancellationToken.None);

        page.Items.Select(item => item.PaymentDetailId)
            .Should().Equal(Id("b"), Id("c"));
    }

    [Fact]
    public async Task A_cursor_on_payment_status_resumes_after_the_boundary()
    {
        var tenantId = await SeedAsync(
            Payment("a", status: "AAA"),
            Payment("b", status: "BBB"),
            Payment("c", status: "CCC"));

        var page = await _repository.QueryAsync(
            Criteria(tenantId) with
            {
                SortBy = PaymentQuerySortFields.PaymentStatus,
                SortDirection = PaymentQuerySortDirections.Ascending,
                CursorBoundary = new PaymentQueryCursorBoundary(Id("a"), "AAA", null, null)
            },
            CancellationToken.None);

        page.Items.Select(item => item.PaymentDetailId)
            .Should().Equal(Id("b"), Id("c"));
    }

    [Fact]
    public async Task The_projected_record_carries_the_display_fields()
    {
        var paymentDate = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var tenantId = await SeedAsync(
            Payment(
                "p1",
                "stripe",
                42.55m,
                currencyCode: "EUR",
                status: PaymentStatuses.Authorized,
                paymentDate: paymentDate));

        var record = (await _repository.QueryAsync(
            Criteria(tenantId),
            CancellationToken.None)).Items.Single();

        record.ProviderName.Should().Be("stripe");
        record.Amount.Should().Be(42.55m);
        record.CurrencyCode.Should().Be("EUR");
        record.PaymentStatus.Should().Be(PaymentStatuses.Authorized);
        record.PaymentDateUtc.Should().BeCloseTo(paymentDate, TimeSpan.FromSeconds(1));
    }
}
