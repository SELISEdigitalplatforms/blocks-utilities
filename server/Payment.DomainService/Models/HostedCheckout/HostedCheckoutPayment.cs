using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class HostedCheckoutPayment
{
    [JsonPropertyName("pspReference")]
    public string? PspReference { get; set; }
    [JsonPropertyName("resultCode")]
    public string? ResultCode { get; set; }
    [JsonPropertyName("amount")]
    public ProviderAmount? Amount { get; set; }
    [JsonPropertyName("paymentMethod")]
    public HostedCheckoutPaymentMethod? PaymentMethod { get; set; }
}
