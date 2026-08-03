using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentRefundResponseMapper
{
    PaymentRefundResponse Map(
        string paymentDetailId,
        PaymentRefund refund);
}
