using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>How a credit purchase is paid for.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CreditPaymentPath
{
    /// <summary>The billing account's saved card, charged during the request.</summary>
    StoredCard = 0,

    /// <summary>
    /// A hosted checkout the buyer pays at, which also saves the card. Used when the billing
    /// account has no saved card (spec 003).
    /// </summary>
    Checkout = 1
}
