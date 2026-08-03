using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

public interface IPaymentFundReturnStrategyResolver
{
    PaymentFundReturnDecision Resolve(
        PaymentDetail payment,
        decimal requestedAmount);
}
