using MongoDB.Bson.Serialization.Attributes;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class PaymentProvider
{
    [BsonId]
    public string ItemId { get; set; } = string.Empty;
    public long Version { get; set; }
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
    /// <summary>
    /// Legacy pointers to Key Vault secrets. Retained so documents written before credentials
    /// moved into the document still deserialise; nothing reads them.
    /// </summary>
    public string? ProviderCredentialSecretName { get; set; }
    public string? TenantSecuritySecretName { get; set; }

    /// <summary>
    /// The provider's own credential JSON, encrypted. Same shape that previously lived in the
    /// vault, so the credential models are unchanged.
    /// </summary>
    public string? ProviderSecretsCiphertext { get; set; }

    /// <summary>
    /// This service's own security material for the provider, encrypted: the return-state key
    /// and the shopper reference key.
    /// </summary>
    public string? TenantSecuritySecretsCiphertext { get; set; }

    /// <summary>
    /// Which key ring entry encrypted both blobs. They are always written together, so one id
    /// covers both.
    /// </summary>
    public string? SecretsEncryptionKeyId { get; set; }

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

    /// <summary>
    /// Stripe's <c>pmc_…</c> payment method configuration, when this provider should offer a
    /// configuration other than the account's default. Ignored when
    /// <see cref="CheckoutPaymentMethodTypes"/> names methods explicitly, because Stripe rejects
    /// a session carrying both.
    /// </summary>
    public string? PaymentMethodConfigurationId { get; set; }

    /// <summary>
    /// The payment methods a hosted checkout should offer, named explicitly. Null or empty — the
    /// default — leaves the choice to the provider, which resolves it from the account's own
    /// Dashboard configuration.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="PaymentMethods"/>, which predates this and is read
    /// by nothing: a document written before this field existed may carry provider-specific
    /// names in it, and reinterpreting those as Stripe method types would silently change what a
    /// live checkout offers on deploy.
    /// </remarks>
    public string[]? CheckoutPaymentMethodTypes { get; set; }
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
