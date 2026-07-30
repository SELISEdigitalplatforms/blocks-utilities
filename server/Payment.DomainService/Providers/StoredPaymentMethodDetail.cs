namespace Payment.DomainService.Providers;

/// <summary>What a saved card shows to the shopper. Never carries the card number itself.</summary>
public sealed record StoredPaymentMethodDetail(
    string? Type = null,
    string? Brand = null,
    string? LastFour = null,
    string? ExpiryMonth = null,
    string? ExpiryYear = null,
    string? FundingSource = null,
    string? IssuerCountry = null);
