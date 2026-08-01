using System.Text.Json.Serialization;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Models.Refunds;

public sealed class ProviderRefundRequest
{
    [JsonPropertyName("merchantAccount")]
    public string MerchantAccount { get; init; } =
        string.Empty;

    [JsonPropertyName("amount")]
    public ProviderAmount Amount { get; init; } = new();

    [JsonPropertyName("reference")]
    public string Reference { get; init; } =
        string.Empty;

    /// <summary>
    /// The organization owning the payment being refunded.
    /// </summary>
    /// <remarks>
    /// Carried so a provider that mints its own object for the refund can echo it back. Intake
    /// checks the organization on every inbound event against the payment's, and an event that
    /// cannot echo one is rejected as belonging to another organization. Excluded from
    /// serialisation: Adyen's refund body does not take it, and its events echo it from the
    /// original payment instead.
    /// </remarks>
    [JsonIgnore]
    public string? OrganizationId { get; init; }
}
