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

    /// <summary>
    /// Which organization within the tenant owns this configuration; null for a tenant-level
    /// one. Exposed because the uniqueness index allows the same provider and merchant in two
    /// organizations, and without it those rows are indistinguishable to anyone reading the
    /// list. This is configuration metadata, not a secret.
    /// </summary>
    public string? OrganizationId { get; init; }

    public string ApiBaseUrl { get; init; } = string.Empty;

    public string? ReturnUrl { get; init; }

    public string? FrontendResultUrl { get; init; }

    public string? CountryCode { get; init; }

    public bool ManualCapture { get; init; }

    public int MaxRefundDays { get; init; }

    public string? StoreId { get; init; }

    public bool IsEnabled { get; init; }
}
