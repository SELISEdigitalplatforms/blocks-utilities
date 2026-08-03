namespace Payment.DomainService.Requests;

public sealed class CreatePaymentCaptureRequest
{
    public decimal Amount { get; set; }
}
