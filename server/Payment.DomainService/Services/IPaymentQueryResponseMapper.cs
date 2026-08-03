using Payment.DomainService.Models;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentQueryResponseMapper
{
    PaymentListResponse Map(
        PaymentQueryCriteria criteria,
        PaymentQueryPage page);
}
