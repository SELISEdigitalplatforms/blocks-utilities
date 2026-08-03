using System.Text;
using FluentAssertions;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentQueryCursorCodecTests
{
    private readonly PaymentQueryCursorCodec _codec = new();

    [Theory]
    [InlineData(PaymentQuerySortFields.ProviderName)]
    [InlineData(PaymentQuerySortFields.Amount)]
    [InlineData(PaymentQuerySortFields.PaymentDate)]
    [InlineData(PaymentQuerySortFields.PaymentStatus)]
    public void Cursor_round_trips_typed_boundary(string sortBy)
    {
        var criteria = Criteria(sortBy);
        var record = Record();

        var cursor = _codec.Encode(criteria, record);
        var decoded = _codec.TryDecode(
            cursor,
            criteria,
            out var boundary);

        decoded.Should().BeTrue();
        boundary.Should().NotBeNull();
        boundary!.PaymentDetailId.Should().Be(record.PaymentDetailId);

        if (sortBy == PaymentQuerySortFields.Amount)
            boundary.AmountValue.Should().Be(record.Amount);
        else if (sortBy == PaymentQuerySortFields.PaymentDate)
            boundary.PaymentDateUtc.Should().Be(record.PaymentDateUtc);
        else
            boundary.TextValue.Should().Be(
                sortBy == PaymentQuerySortFields.ProviderName
                    ? record.ProviderName
                    : record.PaymentStatus);
    }

    [Fact]
    public void Cursor_is_rejected_when_filters_change()
    {
        var criteria = Criteria(PaymentQuerySortFields.PaymentDate);
        var cursor = _codec.Encode(criteria, Record());
        var changed = criteria with
        {
            CurrencyCode = "EUR"
        };

        _codec.TryDecode(cursor, changed, out _).Should().BeFalse();
    }

    [Fact]
    public void Cursor_is_rejected_when_sort_changes_or_payload_is_malformed()
    {
        var criteria = Criteria(PaymentQuerySortFields.PaymentDate);
        var cursor = _codec.Encode(criteria, Record());

        _codec.TryDecode(
                cursor,
                criteria with
                {
                    SortDirection = PaymentQuerySortDirections.Ascending
                },
                out _)
            .Should().BeFalse();
        _codec.TryDecode("not+a+base64url+cursor", criteria, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Cursor_with_null_boundary_is_safely_rejected()
    {
        var criteria = Criteria(PaymentQuerySortFields.PaymentDate);
        const string json =
            "{\"version\":1,\"sortBy\":\"paymentDate\",\"sortDirection\":\"desc\",\"boundaryValue\":null,\"paymentDetailId\":\"payment-1\",\"filterFingerprint\":\"value\"}";
        var cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        _codec.TryDecode(cursor, criteria, out _).Should().BeFalse();
    }

    [Fact]
    public void Page_size_change_does_not_invalidate_cursor()
    {
        var criteria = Criteria(PaymentQuerySortFields.PaymentDate);
        var cursor = _codec.Encode(criteria, Record());

        _codec.TryDecode(
                cursor,
                criteria with
                {
                    PageSize = 100
                },
                out _)
            .Should().BeTrue();
    }

    private static PaymentQueryCriteria Criteria(string sortBy) =>
        new()
        {
            TenantId = "tenant-1",
            PageSize = 25,
            ProviderNames = ["ADYEN-ONLINE"],
            PaymentStatuses = [PaymentStatuses.Authorized],
            CurrencyCode = "CHF",
            SortBy = sortBy,
            SortDirection = PaymentQuerySortDirections.Descending
        };

    private static PaymentQueryRecord Record() =>
        new()
        {
            PaymentDetailId = "payment-1",
            ProviderName = "ADYEN-ONLINE",
            Amount = 10.25m,
            CurrencyCode = "CHF",
            PaymentDateUtc = new DateTime(
                2026,
                7,
                20,
                13,
                35,
                39,
                DateTimeKind.Utc),
            PaymentStatus = PaymentStatuses.Authorized
        };
}
