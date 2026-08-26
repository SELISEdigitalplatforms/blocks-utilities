namespace Subscription.DomainService.Requests;

/// <summary>
/// The subscriber identity every financial document for this organization will be addressed to.
/// </summary>
/// <remarks>
/// A whole-profile write rather than a patch. The fields describe one legal identity and editing them
/// one at a time invites a half-changed address — a new street with an old city — on the next document
/// issued.
/// </remarks>
public sealed class UpdateBillingProfileRequest
{
    /// <summary>The name the organization contracts under. Required: a document has to carry one.</summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>What the organization calls itself, when that differs. Falls back to the legal name.</summary>
    public string? DisplayName { get; set; }

    public string BillingContactName { get; set; } = string.Empty;

    public string BillingContactEmail { get; set; } = string.Empty;

    public BillingAddressRequest? Address { get; set; }

    public string? TaxRegistrationId { get; set; }

    /// <summary>
    /// Honoured only for the platform console, using the standard subscription organization
    /// resolution policy.
    /// </summary>
    public string? OrganizationId { get; set; }
}

public sealed class BillingAddressRequest
{
    public string? Line1 { get; set; }

    public string? Line2 { get; set; }

    public string? City { get; set; }

    public string? Region { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? CountryCode { get; set; }
}
