using FluentAssertions;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentQueryResponseMapperTests
{
    private readonly PaymentQueryResponseMapper _mapper =
        new(new PaymentQueryCursorCodec());

    [Fact]
    public void Forward_first_page_exposes_only_a_next_page()
    {
        var response = _mapper.Map(
            Criteria(),
            new PaymentQueryPage([Record()], true));

        response.Items.Should().ContainSingle();
        response.PageInfo.HasNextPage.Should().BeTrue();
        response.PageInfo.HasPreviousPage.Should().BeFalse();
        response.PageInfo.StartCursor.Should().NotBeNullOrWhiteSpace();
        response.PageInfo.EndCursor.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Backward_page_exposes_navigation_in_both_directions()
    {
        var criteria = Criteria() with
        {
            IsBackward = true,
            CursorBoundary = new PaymentQueryCursorBoundary(
                "payment-2",
                null,
                null,
                DateTime.UtcNow)
        };

        var response = _mapper.Map(
            criteria,
            new PaymentQueryPage([Record()], true));

        response.PageInfo.HasPreviousPage.Should().BeTrue();
        response.PageInfo.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void Empty_page_has_no_cursors()
    {
        var response = _mapper.Map(
            Criteria(),
            new PaymentQueryPage([], false));

        response.Items.Should().BeEmpty();
        response.PageInfo.StartCursor.Should().BeNull();
        response.PageInfo.EndCursor.Should().BeNull();
    }

    private static PaymentQueryCriteria Criteria() =>
        new()
        {
            TenantId = "tenant-1",
            PageSize = 25,
            SortBy = PaymentQuerySortFields.PaymentDate,
            SortDirection = PaymentQuerySortDirections.Descending
        };

    private static PaymentQueryRecord Record() =>
        new()
        {
            PaymentDetailId = "payment-1",
            ProviderName = "ADYEN-ONLINE",
            Amount = 10,
            CurrencyCode = "CHF",
            PaymentDateUtc = DateTime.UtcNow,
            PaymentStatus = PaymentStatuses.Authorized
        };
}
