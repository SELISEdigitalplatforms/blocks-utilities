namespace Payment.DomainService.Requests;

/// <summary>
/// Registers a payment provider for the calling tenant.
/// </summary>
/// <remarks>
/// The tenant is taken from the execution context, never from this request. Neither the return
/// URL nor, for providers with a fixed host, the API base is accepted here: a caller-supplied
/// return URL could redirect the payment flow elsewhere.
/// </remarks>
public sealed class RegisterPaymentProviderRequest
{
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Which organization within the tenant this configuration belongs to. Omit it to use the
    /// caller's own organization.
    /// </summary>
    /// <remarks>
    /// Accepted here, unlike the tenant, because the configuration console runs with a fixed
    /// default organization and would otherwise be unable to configure any other. A named
    /// organization is verified against IAM under the caller's own token before anything is
    /// written, so this is not a way to reach an organization the caller cannot already see.
    ///
    /// It is identity, not configuration: the value decides which key ring encrypts the
    /// credentials, so it cannot be changed afterwards without re-encrypting them.
    /// </remarks>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Several organizations to configure identically, in one request. Unioned with
    /// <see cref="OrganizationId"/> and de-duplicated.
    /// </summary>
    /// <remarks>
    /// A tenant whose organizations all bill through the same merchant account would otherwise
    /// have to repeat the whole registration — credentials included — once per organization,
    /// and a configuration is not shared between them: each is encrypted under its own key ring
    /// and stored as its own row, which is what lets one be disabled or re-keyed without
    /// touching the rest.
    /// <para>
    /// Each is attempted independently. One failing does not roll back the others, so the
    /// response reports every organization separately.
    /// </para>
    /// </remarks>
    public string[] OrganizationIds { get; set; } = [];

    /// <summary>Merchant or account identifier at the provider, echoed back on its webhooks.</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Where the shopper is sent once the payment result is known.</summary>
    public string FrontendResultUrl { get; set; } = string.Empty;

    /// <summary>Required only for providers whose host varies by environment, such as Adyen.</summary>
    public string? ApiBaseUrl { get; set; }

    public string? CountryCode { get; set; }

    public bool ManualCapture { get; set; }

    public int MaxRefundDays { get; set; }

    public string? StoreId { get; set; }

    /// <summary>Provider API key. Stripe's secret key, or Adyen's Checkout API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Signing secret for the provider's main webhook. Stripe's endpoint secret, or Adyen's
    /// standard notification HMAC key.
    /// </summary>
    public string WebhookHmacKey { get; set; } = string.Empty;

    /// <summary>
    /// Signing secret for the provider's token webhook, where it has a separate one. Adyen
    /// only; Stripe signs every event with the same secret.
    /// </summary>
    public string? TokenHmacKey { get; set; }

    /// <summary>
    /// Existing return-state key, supplied only when migrating a provider that already has
    /// one. Generated when omitted.
    /// </summary>
    public string? ReturnStateHmacKey { get; set; }

    /// <summary>
    /// Existing shopper reference key, supplied only when migrating. This derives shopper
    /// references, and stored payment methods are keyed by them, so supplying the current
    /// value is what keeps saved cards resolving. Generated when omitted.
    /// </summary>
    public string? ShopperReferenceHmacKey { get; set; }
}
