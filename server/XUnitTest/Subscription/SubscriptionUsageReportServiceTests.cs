using FluentAssertions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// Reading the tenant-usage-analytics rollups, and the query validation and cursor scoping that
/// keep one filtered page from being read as another.
/// </summary>
public sealed class SubscriptionUsageReportServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionContextResolver> _context = new();
    private readonly Mock<ISubscriptionUsageActivityRollupRepository> _activity = new();
    private readonly Mock<ISubscriptionUsageActorRollupRepository> _actors = new();
    private readonly Mock<ISubscriptionUsageInvoiceRepository> _invoices = new();
    private readonly Mock<ISubscriptionUsageCurrentRepository> _current = new();

    public SubscriptionUsageReportServiceTests()
    {
        _context
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _activity
            .Setup(repository => repository.SumByPeriodAsync(
                TenantId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<BillingInterval>(),
                It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageTimeseriesPage([], false));

        _activity
            .Setup(repository => repository.SumByOrganizationAsync(
                TenantId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<UsageOrganizationTotalsCursor?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageOrganizationTotalsPage([], false));

        _actors
            .Setup(repository => repository.ListAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<UsageActorRollupCursor?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageActorRollupPage([], false));

        _invoices
            .Setup(repository => repository.ListAsync(
                TenantId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<UsageInvoiceCursor?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageInvoicePage([], false));

        _current
            .Setup(repository => repository.ListByOrganizationAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task A_page_size_above_the_maximum_is_refused()
    {
        var result = await Service().GetTimeseriesAsync(
            new GetUsageReportRequest { PageSize = 101 }, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("subscription_usage_report_query_invalid");
    }

    /// <summary>
    /// Zero (and any other non-positive value) is not a bound violation — it means "unspecified"
    /// and falls back to the default page size, the same convention <c>GetUsageReportRequest</c>'s
    /// own default expresses.
    /// </summary>
    [Fact]
    public async Task A_page_size_of_zero_falls_back_to_the_default_rather_than_being_refused()
    {
        var result = await Service().GetTimeseriesAsync(
            new GetUsageReportRequest { PageSize = 0 }, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PageInfo.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task An_inverted_date_range_is_refused()
    {
        var result = await Service().GetTimeseriesAsync(
            new GetUsageReportRequest
            {
                FromUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ToUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_usage_report_query_invalid");
    }

    [Fact]
    public async Task An_unrecognized_granularity_is_refused()
    {
        var result = await Service().GetTimeseriesAsync(
            new GetUsageReportRequest { Granularity = "Fortnight" },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_usage_report_query_invalid");
    }

    [Fact]
    public async Task A_valid_granularity_is_parsed_and_passed_through_to_the_repository()
    {
        var result = await Service().GetTimeseriesAsync(
            new GetUsageReportRequest { Granularity = "week" },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _activity.Verify(repository => repository.SumByPeriodAsync(
            TenantId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), BillingInterval.Week,
            It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Getting_actors_without_an_organization_id_is_refused()
    {
        var result = await Service().GetActorsAsync(
            new GetUsageReportRequest { OrganizationId = null }, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_usage_report_query_invalid");
    }

    [Fact]
    public async Task Getting_allowances_without_an_organization_id_is_refused()
    {
        var result = await Service().GetAllowancesAsync(
            new GetUsageReportRequest { OrganizationId = null }, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_usage_report_query_invalid");
    }

    [Fact]
    public async Task Getting_actors_with_an_organization_id_succeeds()
    {
        var result = await Service().GetActorsAsync(
            new GetUsageReportRequest { OrganizationId = OrganizationId },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// A cursor issued under one filter set (one MeterKey) must be refused when presented back
    /// under a different filter set (a different MeterKey) — directly against the codec.
    /// </summary>
    [Fact]
    public void A_cursor_issued_under_one_meter_key_is_refused_under_a_different_meter_key()
    {
        var issuedScope = new UsageReportCursorScope(
            TenantId, OrganizationId, null, "screening", "Month", null, null);
        var cursor = UsageReportCursorCodec.Encode(issuedScope, "2026-08-01T00:00:00.0000000Z");

        var presentedScope = issuedScope with { MeterKey = "envelope" };

        UsageReportCursorCodec.TryDecode(cursor, presentedScope, out _).Should().BeFalse();
        UsageReportCursorCodec.TryDecode(cursor, issuedScope, out var boundary).Should().BeTrue();
        boundary.Should().Be("2026-08-01T00:00:00.0000000Z");
    }

    /// <summary>
    /// The same scoping, exercised through the service: a cursor from a first page under one
    /// MeterKey is refused when the second call names a different MeterKey.
    /// </summary>
    [Fact]
    public async Task A_cursor_from_the_service_is_refused_when_the_meter_key_filter_changes()
    {
        _activity
            .Setup(repository => repository.SumByPeriodAsync(
                TenantId, It.IsAny<string?>(), It.IsAny<string?>(), "screening",
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<BillingInterval>(),
                It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageTimeseriesPage(
                [new UsageTimeseriesBucket(
                    new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), 10, 2)],
                true));

        var first = await Service().GetTimeseriesAsync(
            new GetUsageReportRequest { MeterKey = "screening" }, "corr-1", CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value!.PageInfo.HasNextPage.Should().BeTrue();
        var cursor = first.Value.PageInfo.NextCursor;
        cursor.Should().NotBeNullOrEmpty();

        var second = await Service().GetTimeseriesAsync(
            new GetUsageReportRequest { MeterKey = "envelope", After = cursor },
            "corr-1",
            CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.ErrorCode.Should().Be("subscription_usage_report_query_invalid");
    }

    [Fact]
    public async Task A_successful_timeseries_call_maps_repository_results_into_the_response()
    {
        _activity
            .Setup(repository => repository.SumByPeriodAsync(
                TenantId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<BillingInterval>(),
                It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageTimeseriesPage(
                [new UsageTimeseriesBucket(
                    new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), 42.5m, 7)],
                false));

        var result = await Service().GetTimeseriesAsync(
            new GetUsageReportRequest { Granularity = "Month" }, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var point = result.Value!.Items.Should().ContainSingle().Which;
        point.PeriodStartUtc.Should().Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        point.ConsumedQuantity.Should().Be(42.5m);
        point.EntryCount.Should().Be(7);
        result.Value.PageInfo.HasNextPage.Should().BeFalse();
        result.Value.PageInfo.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task A_successful_organizations_call_maps_repository_results_into_the_response()
    {
        _activity
            .Setup(repository => repository.SumByOrganizationAsync(
                TenantId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<UsageOrganizationTotalsCursor?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageOrganizationTotalsPage(
                [new UsageOrganizationTotal("org-9", 15m, 3)], false));

        var result = await Service().GetOrganizationsAsync(
            new GetUsageReportRequest(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Items.Should().ContainSingle().Which;
        item.OrganizationId.Should().Be("org-9");
        item.ConsumedQuantity.Should().Be(15m);
        item.EntryCount.Should().Be(3);
    }

    private ISubscriptionUsageReportService Service() => new SubscriptionUsageReportService(
        _context.Object,
        _activity.Object,
        _actors.Object,
        _invoices.Object,
        _current.Object);
}
