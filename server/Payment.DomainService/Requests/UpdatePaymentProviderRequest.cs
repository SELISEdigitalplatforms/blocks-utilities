using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payment.DomainService.Requests;

/// <summary>
/// Replaces the editable, non-secret configuration of an existing payment
/// provider. Provider identity, endpoints, and credentials are intentionally
/// not part of this contract.
/// </summary>
public sealed class UpdatePaymentProviderRequest
{
    public long? Version { get; set; }

    public string FrontendResultUrl { get; set; } = string.Empty;

    public string? CountryCode { get; set; }

    public bool ManualCapture { get; set; }

    /// <summary>
    /// Hours the provider waits before capturing an authorised payment. <c>0</c> says the
    /// account captures immediately, which is the only way a provider that sends no capture
    /// notification of its own (Adyen on automatic capture) can be told the funds were taken.
    /// Omit when unknown: the payment then stays authorised until a capture event arrives.
    /// </summary>
    public int? CaptureDelayHours { get; set; }

    public int MaxRefundDays { get; set; }

    public string? StoreId { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>
    /// Stripe's <c>pmc_…</c> payment method configuration, when the checkout should offer one
    /// other than the account's default. Mutually exclusive with
    /// <c>CheckoutPaymentMethodTypes</c>.
    /// </summary>
    public string? PaymentMethodConfigurationId { get; set; }

    /// <summary>
    /// The payment methods the hosted checkout should offer, e.g. <c>card</c>, <c>twint</c>,
    /// <c>paypal</c>, <c>klarna</c>. Omit to let the provider resolve them from its own
    /// Dashboard configuration, which is the default and what every existing provider does.
    /// </summary>
    public string[]? CheckoutPaymentMethodTypes { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnmappedFields { get; set; }
}
