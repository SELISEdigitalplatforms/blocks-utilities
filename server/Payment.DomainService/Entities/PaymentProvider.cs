using MongoDB.Bson.Serialization.Attributes;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class PaymentProvider
{
    [BsonId]
    public string ItemId { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? ProviderKey { get; set; }
    public string? CustomerId { get; set; }
    public string? TerminalId { get; set; }
    public string? JsonApiUserName { get; set; }
    public string? JsonApiPassword { get; set; }
    public string? SpecVersion { get; set; }
    public int RetryIndicator { get; set; }
    public bool DisplayBillingAddressForm { get; set; }
    public string[]? BillingAddressFormMandatoryFields { get; set; }
    public bool DisplayDeliveryAddressForm { get; set; }
    public string[]? DeliveryAddressFormMandatoryFields { get; set; }
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string? RefundApiBaseUrl { get; set; }
    public string? SuccessUrl { get; set; }
    public string? FailUrl { get; set; }
    public string? AbortUrl { get; set; }
    public string? NotificationUrl { get; set; }
    public string? ReturnUrl { get; set; }
    public string? FrontendResultUrl { get; set; }
    public string? ProviderCredentialSecretName { get; set; }
    public string? TenantSecuritySecretName { get; set; }

    [BsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ReturnStateHmacKey { get; set; }

    [BsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PreviousReturnStateHmacKey { get; set; }

    [BsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? StandardWebhookHmacKey { get; set; }

    [BsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PreviousStandardWebhookHmacKey { get; set; }

    [BsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? TokenWebhookHmacKey { get; set; }

    [BsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PreviousTokenWebhookHmacKey { get; set; }

    [BsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ShopperReferenceHmacKey { get; set; }
    public string[]? PaymentMethods { get; set; }
    public bool AllowRecurringFlagFromPayload { get; set; }
    public bool IsRecurring { get; set; }
    public Dictionary<string, object>? Utilities { get; set; }
    public string? SecretKey { get; set; }
    public string? OrganizationId { get; set; }
    public string MerchantId { get; set; } = string.Empty;
    public string? Token { get; set; }

    [BsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;
    public bool TerminalSinglePage { get; set; }
    public string? RedirectUrl { get; set; }
    public string? PayableAmountName { get; set; }
    public string? StoreId { get; set; }
    public string? StorePassword { get; set; }
    public string? IpnUrl { get; set; }
    public string? ThemeId { get; set; }
    public bool ManualCapture { get; set; }
    public int? CaptureDelayHours { get; set; }
    public string[]? Wallets { get; set; }
    public bool IsProductionEnvironment { get; set; }
    public string? LogoImageUrl { get; set; }
    public string? SiteId { get; set; }
    public string? CountryCode { get; set; }
    public string? ChainId { get; set; }
    public string? CustomHeaderName { get; set; }
    public string? CustomHeaderValue { get; set; }
    public int MaxRefundDays { get; set; }
    public string? PaymentAdapterType { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string[]? AllowedCallbackHosts { get; set; }
}
