namespace Subscription.DomainService.Requests;

/// <summary>
/// The legal identity this tenant issues its financial documents under.
/// </summary>
/// <remarks>
/// Tenant-scoped, with no organization: the seller is the tenant, and letting an organization name
/// its own seller would let a subscriber decide who invoiced them.
/// </remarks>
public sealed class UpdateMerchantProfileRequest
{
    /// <summary>The registered name of the selling entity. Required — an invoice names a seller.</summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>The trading name, where it differs from the registered one.</summary>
    public string? DisplayName { get; set; }

    public BillingAddressRequest? Address { get; set; }

    /// <summary>The seller's own VAT or tax registration.</summary>
    public string? TaxRegistrationId { get; set; }

    public string? SupportEmail { get; set; }

    /// <summary>Printed under the totals: bank details, terms, a remittance reference.</summary>
    public string? PaymentInstructions { get; set; }

    /// <summary>The storage id an upload through the Storage service returned. Null clears the logo.</summary>
    public string? LogoFileId { get; set; }

    /// <summary>A six-digit hex color, with or without the leading <c>#</c>. Null uses the shared default.</summary>
    public string? PrimaryColor { get; set; }

    /// <summary>A six-digit hex color, with or without the leading <c>#</c>. Null uses the shared default.</summary>
    public string? AccentColor { get; set; }
}
