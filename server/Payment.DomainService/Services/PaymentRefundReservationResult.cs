using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record PaymentRefundReservationResult(
    PaymentDetail? Payment,
    PaymentRefund? Refund,
    string? LeaseId,
    PaymentRefundOperationResult? TerminalResult)
{
    public bool CanSubmit =>
        Payment != null &&
        Refund != null &&
        LeaseId != null &&
        TerminalResult == null;
}
