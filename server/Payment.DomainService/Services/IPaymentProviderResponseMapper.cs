using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentProviderResponseMapper
{
    PaymentProviderResponse Map(PaymentProvider provider);
}
