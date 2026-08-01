using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Models;

/// <summary>
/// The provider request captured before it is sent, so an interrupted initiation can be
/// replayed verbatim by <c>RecoverAsync</c>.
/// </summary>
/// <remarks>
/// The named fields are the ones the rest of the system reasons about — webhook ownership
/// checks and checkout result validation compare against <see cref="Reference"/> and
/// <see cref="MerchantAccount"/> without knowing which provider produced them.
/// <see cref="Payload"/> carries the provider's own request shape and is opaque to everything
/// except the client that will send it.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class ProviderInitiationRequest
{
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Reference echoed back by the provider, used to route its webhooks home.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Merchant or account identifier the payment was submitted under, when the provider has one.</summary>
    public string? MerchantAccount { get; set; }

    public long AmountMinorUnits { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>
    /// How this payment will be captured, as a <see cref="Enums.PaymentCaptureModes"/> value.
    /// Each provider expresses capture differently in its own request, so the provider's
    /// factory resolves the intent and the rest of the system reads it from here.
    /// </summary>
    public string CaptureMode { get; set; } = PaymentCaptureModes.AccountDefault;

    public int? CaptureDelayHours { get; set; }

    /// <summary>Optional merchant site identifier carried through to the payment record.</summary>
    public string? SiteId { get; set; }

    /// <summary>The provider's own request body, opaque outside that provider's client.</summary>
    public BsonDocument Payload { get; set; } = [];
}
