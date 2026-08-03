using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Services;

public sealed class PaymentFundReturnStrategyResolver :
    IPaymentFundReturnStrategyResolver
{
    public PaymentFundReturnDecision Resolve(
        PaymentDetail payment,
        decimal requestedAmount)
    {
        var capturedAvailable = Math.Max(
            0,
            payment.CapturedAmount -
            payment.RefundedAmount -
            payment.ReservedRefundAmount);

        if (capturedAvailable >= requestedAmount)
        {
            return new PaymentFundReturnDecision(
                true,
                PaymentFundReturnOperations.Refund);
        }

        var isFullOriginalAmount =
            requestedAmount == payment.PreciseAmount &&
            payment.RefundedAmount == 0 &&
            payment.ReservedRefundAmount == 0;

        if (isFullOriginalAmount)
        {
            return new PaymentFundReturnDecision(
                true,
                PaymentFundReturnOperations.Reversal);
        }

        return new PaymentFundReturnDecision(
            false,
            string.Empty,
            "payment_not_captured",
            "The requested amount has not been captured. Capture it before requesting a partial refund.");
    }
}
