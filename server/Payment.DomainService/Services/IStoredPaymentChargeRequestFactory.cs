using Payment.DomainService.Entities;
using Payment.DomainService.Models.StoredPayment;

namespace Payment.DomainService.Services;

public interface IStoredPaymentChargeRequestFactory
{
    StoredPaymentChargeRequest Create(
        PaymentDetail payment,
        PaymentProvider provider,
        string providerReference,
        string providerToken,
        long minorUnits);
}
