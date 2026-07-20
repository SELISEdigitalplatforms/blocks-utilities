using Payment.DomainService.Entities;
using Payment.DomainService.Models.Refunds;

namespace Payment.DomainService.Services;

public interface IPaymentRefundRequestFactory
{
    ProviderRefundRequest Create(
        PaymentRefund refund,
        long minorUnits);

    ProviderReversalRequest CreateReversal(
        PaymentRefund refund);
}
