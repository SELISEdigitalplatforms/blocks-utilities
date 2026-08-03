using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed record PaymentCaptureReservationResult(
    PaymentDetail? Payment,
    PaymentCapture? Capture,
    string? LeaseId,
    PaymentCaptureOperationResult? TerminalResult)
{
    public bool CanSubmit => TerminalResult == null;
}
