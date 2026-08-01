using Payment.DomainService.Entities;
using Payment.DomainService.Models.Refunds;

namespace Payment.DomainService.Services;

public interface IPaymentRefundRequestFactory
{
    /// <summary>
    /// Takes the payment as well as the refund, because the organization a refund belongs to
    /// lives on the payment and has to travel out with the request.
    /// </summary>
    ProviderRefundRequest Create(
        PaymentDetail payment,
        PaymentRefund refund,
        long minorUnits);

    ProviderReversalRequest CreateReversal(
        PaymentRefund refund);
}
