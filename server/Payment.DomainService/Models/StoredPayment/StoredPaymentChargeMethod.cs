using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.StoredPayment;

public sealed class StoredPaymentChargeMethod
{
    [JsonPropertyName("storedPaymentMethodId")]
    public string StoredPaymentMethodId { get; init; } = string.Empty;
}
