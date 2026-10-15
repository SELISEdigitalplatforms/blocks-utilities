using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>What a discount code may be redeemed against.</summary>
/// <remarks>
/// One scope per code, never both. A subscription discount can run for several periods and
/// interacts with a price's automatic and volume discounts; a credit purchase is a single charge
/// with neither, so a code written for one would mean something different on the other.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiscountScope
{
    /// <summary>Signup and plan changes. Every discount stored before scopes existed reads as this.</summary>
    Subscription = 0,

    /// <summary>A one-off purchase of meter credits, and nothing else.</summary>
    CreditPurchase = 1
}
