using System.Text.Json.Serialization;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Models.StoredPayment;

public sealed class StoredPaymentChargeResponse
{
    [JsonPropertyName("pspReference")]
    public string? PspReference { get; set; }

    [JsonPropertyName("merchantReference")]
    public string? MerchantReference { get; set; }

    [JsonPropertyName("resultCode")]
    public string? ResultCode { get; set; }

    [JsonPropertyName("amount")]
    public ProviderAmount? Amount { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }
}
