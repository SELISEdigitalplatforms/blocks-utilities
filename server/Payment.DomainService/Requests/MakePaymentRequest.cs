using System.Text.Json.Serialization;

namespace Payment.DomainService.Requests;

public sealed class MakePaymentRequest
{
    public string ProviderName { get; set; } = "ADYEN-ONLINE";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PaymentMeansAliasId { get; set; }
    public bool? SavePaymentMethod { get; set; }
    public bool? RememberCard { get; set; }
    public string Language { get; set; } = "en";
    public bool IsRecurring { get; set; }
    public string? RecurringModel { get; set; }
    public string? PaymentMeansCustomerId { get; set; }
    public string? PaymentMeansPaymentMethodId { get; set; }
    public string? TransactionId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerCity { get; set; }
    public string? CustomerPostCode { get; set; }
    public string? CustomerCountry { get; set; }
    public string? CustomerPhone { get; set; }
    public string? ProductName { get; set; }
    public string? ProductCategory { get; set; }
    public string? ProductProfile { get; set; }
    public string? CustomerOrganizationId { get; set; }

    /// <summary>
    /// Which organization within the tenant this payment belongs to. Omit it to use the
    /// caller's own organization.
    /// </summary>
    /// <remarks>
    /// Not the same thing as <see cref="CustomerOrganizationId"/>, which describes the
    /// shopper and is carried through as data. This one decides which merchant account takes
    /// the money: provider lookup keys off the payment's organization, so a payment stamped
    /// with one organization resolves that organization's provider.
    /// <para>
    /// Verified the same way a registration's organization is, through the shared resolver,
    /// so both endpoints trust exactly the same set of organizations.
    /// </para>
    /// </remarks>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// The exact <see cref="Entities.PaymentProvider"/> item id this payment is expected to
    /// resolve and execute against, when the caller already froze one -- e.g. a subscription's
    /// billing account. Never bound from an HTTP body: <see cref="JsonIgnoreAttribute"/> keeps a
    /// public caller from setting it, since only an internal caller that already knows the
    /// expected provider row has any business asserting it.
    /// </summary>
    /// <remarks>
    /// When set, initiation refuses to contact the provider at all if the scope-fallback chain
    /// resolves a different row than expected -- fail closed, before any external call, rather
    /// than resolving independently and comparing after the fact. Null preserves the previous,
    /// unchecked behaviour for every caller that has never frozen a provider identity.
    /// </remarks>
    [JsonIgnore]
    public string? ExpectedProviderId { get; set; }

    [JsonIgnore]
    public bool ShouldSavePaymentMethod =>
        SavePaymentMethod ??
        RememberCard ??
        false;

    [JsonIgnore]
    public bool HasConflictingSavePaymentPreferences =>
        SavePaymentMethod.HasValue &&
        RememberCard.HasValue &&
        SavePaymentMethod.Value != RememberCard.Value;
}
