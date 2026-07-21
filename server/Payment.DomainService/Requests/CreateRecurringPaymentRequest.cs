namespace Payment.DomainService.Requests;

public sealed class CreateRecurringPaymentRequest
{
    public string ProviderName { get; set; } = "ADYEN-ONLINE";

    public string StoredPaymentMethodId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public string RecurringProcessingModel { get; set; } = "Subscription";

    public string? Description { get; set; }
}
