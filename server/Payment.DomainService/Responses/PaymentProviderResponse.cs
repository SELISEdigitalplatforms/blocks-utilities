namespace Payment.DomainService.Responses;

/// <summary>
/// Safe payment-provider configuration. Credential material and tenant
/// security secrets are never exposed.
/// </summary>
public sealed class PaymentProviderResponse
{
    public string PaymentProviderId { get; init; } = string.Empty;

    public long Version { get; init; }

    public string ProviderName { get; init; } = string.Empty;

    public string MerchantId { get; init; } = string.Empty;

    public string ApiBaseUrl { get; init; } = string.Empty;

    public string? ReturnUrl { get; init; }

    public string? FrontendResultUrl { get; init; }

    public string? CountryCode { get; init; }

    public bool ManualCapture { get; init; }

    public int MaxRefundDays { get; init; }

    public string? StoreId { get; init; }

    public bool IsEnabled { get; init; }
}
