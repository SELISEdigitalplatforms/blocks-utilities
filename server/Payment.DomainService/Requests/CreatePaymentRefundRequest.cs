namespace Payment.DomainService.Requests;

public sealed class CreatePaymentRefundRequest
{
    public decimal Amount { get; set; }

    public string? Reason { get; set; }
}
