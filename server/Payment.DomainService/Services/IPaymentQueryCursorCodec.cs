using Payment.DomainService.Models;

namespace Payment.DomainService.Services;

public interface IPaymentQueryCursorCodec
{
    string Encode(
        PaymentQueryCriteria criteria,
        PaymentQueryRecord record);

    bool TryDecode(
        string cursor,
        PaymentQueryCriteria criteria,
        out PaymentQueryCursorBoundary? boundary);
}
