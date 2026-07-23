using Payment.DomainService.Models;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class PaymentQueryResponseMapper :
    IPaymentQueryResponseMapper
{
    private readonly IPaymentQueryCursorCodec _cursorCodec;

    public PaymentQueryResponseMapper(
        IPaymentQueryCursorCodec cursorCodec)
    {
        _cursorCodec = cursorCodec;
    }

    public PaymentListResponse Map(
        PaymentQueryCriteria criteria,
        PaymentQueryPage page)
    {
        var items = page.Items
            .Select(record => new PaymentListItemResponse
            {
                PaymentDetailId = record.PaymentDetailId,
                ProviderName = record.ProviderName,
                Amount = record.Amount,
                CurrencyCode = record.CurrencyCode,
                PaymentDateUtc = record.PaymentDateUtc.ToUniversalTime(),
                PaymentStatus = record.PaymentStatus,
                HasPendingRefund = record.HasPendingRefund
            })
            .ToArray();
        var first = page.Items.FirstOrDefault();
        var last = page.Items.LastOrDefault();

        return new PaymentListResponse
        {
            Items = items,
            PageInfo = new CursorPageInfoResponse
            {
                PageSize = criteria.PageSize,
                HasPreviousPage = criteria.IsBackward
                    ? page.HasMoreInQueryDirection
                    : criteria.CursorBoundary != null,
                HasNextPage = criteria.IsBackward
                    ? criteria.CursorBoundary != null
                    : page.HasMoreInQueryDirection,
                StartCursor = first == null
                    ? null
                    : _cursorCodec.Encode(criteria, first),
                EndCursor = last == null
                    ? null
                    : _cursorCodec.Encode(criteria, last)
            }
        };
    }
}
