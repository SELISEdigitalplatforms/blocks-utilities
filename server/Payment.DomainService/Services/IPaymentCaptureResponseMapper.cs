using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentCaptureResponseMapper
{
    PaymentCaptureResponse Map(
        string paymentDetailId,
        PaymentCapture capture);
}
