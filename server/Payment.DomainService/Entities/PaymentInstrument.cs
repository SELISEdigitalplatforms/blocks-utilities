using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class PaymentInstrument
{
    public string? Type { get; set; }
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }

    /// <summary>The name on the card that paid, as the provider reported it.</summary>
    public string? HolderName { get; set; }
    public string? FundingSource { get; set; }
    public string? IssuerCountry { get; set; }
    public string? IssuerName { get; set; }
    public string? AuthorizationCode { get; set; }
}
