using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class PaymentRefundResponseMapper :
    IPaymentRefundResponseMapper
{
    public PaymentRefundResponse Map(
        string paymentDetailId,
        PaymentRefund refund) =>
        new()
        {
            RefundId = refund.RefundId,
            PaymentDetailId = paymentDetailId,
            Status = refund.Status,
            Operation = refund.ProviderOperation,
            CompletionAction = refund.CompletionAction,
            Amount = refund.Amount,
            CurrencyCode = refund.CurrencyCode,
            FailureCode = refund.FailureCode,
            FailureSummary = refund.FailureSummary,
            CreatedAtUtc = refund.CreatedAtUtc,
            CompletedAtUtc = refund.CompletedAtUtc
        };
}
