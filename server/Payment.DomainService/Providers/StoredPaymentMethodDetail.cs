namespace Payment.DomainService.Providers;

/// <summary>What a saved card shows to the shopper. Never carries the card number itself.</summary>
/// <param name="CardFingerprint">
/// The provider's stable identifier for the card itself, the same across every token it mints
/// for that card. Null where the provider does not report one.
/// </param>
public sealed record StoredPaymentMethodDetail(
    string? Type = null,
    string? Brand = null,
    string? LastFour = null,
    string? ExpiryMonth = null,
    string? ExpiryYear = null,
    string? FundingSource = null,
    string? IssuerCountry = null,
    string? CardFingerprint = null);
