using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed class PaymentInstrumentResponse
{
    public string? Type { get; init; }
    public string? Brand { get; init; }
    public string? LastFour { get; init; }
    public string? ExpiryMonth { get; init; }
    public string? ExpiryYear { get; init; }
    public string? FundingSource { get; init; }
    public string? IssuerCountry { get; init; }
    public string? IssuerName { get; init; }
}
