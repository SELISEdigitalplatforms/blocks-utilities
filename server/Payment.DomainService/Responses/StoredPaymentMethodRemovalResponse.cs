namespace Payment.DomainService.Responses;

public sealed class StoredPaymentMethodRemovalResponse
{
    public string PaymentMethodId { get; init; } = string.Empty;

    public string Status { get; init; } = "REMOVAL_PENDING";
}
