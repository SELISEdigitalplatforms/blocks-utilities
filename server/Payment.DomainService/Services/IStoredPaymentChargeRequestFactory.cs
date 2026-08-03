using Payment.DomainService.Entities;
using Payment.DomainService.Models.StoredPayment;

namespace Payment.DomainService.Services;

public interface IStoredPaymentChargeRequestFactory
{
    /// <summary>
    /// Builds the charge request. Takes the stored method rather than only its decrypted token,
    /// because some providers need the payer identifier held alongside it.
    /// </summary>
    StoredPaymentChargeRequest Create(
        PaymentDetail payment,
        PaymentProvider provider,
        StoredPaymentMethod method,
        string providerReference,
        string providerToken,
        long minorUnits);
}
