namespace Payment.DomainService.Requests;

public sealed class MakePaymentRequest
{
    public string ProviderName { get; set; } = "ADYEN-ONLINE";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PaymentMeansAliasId { get; set; }
    public bool RememberCard { get; set; }
    public string Language { get; set; } = "en";
    public bool IsRecurring { get; set; }
    public string? RecurringModel { get; set; }
    public string? PaymentMeansCustomerId { get; set; }
    public string? PaymentMeansPaymentMethodId { get; set; }
    public string? TransactionId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerCity { get; set; }
    public string? CustomerPostCode { get; set; }
    public string? CustomerCountry { get; set; }
    public string? CustomerPhone { get; set; }
    public string? ProductName { get; set; }
    public string? ProductCategory { get; set; }
    public string? ProductProfile { get; set; }
    public string? CustomerOrganizationId { get; set; }
}
