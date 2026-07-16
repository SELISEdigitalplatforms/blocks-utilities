using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentResponseMapper
{
    PaymentResponse Map(PaymentDetail payment);
}
