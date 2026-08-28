using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// Who is selling: the legal identity a tenant issues its invoices and credit notes under.
/// </summary>
/// <remarks>
/// Stored per tenant rather than read from configuration, because this platform runs many tenants
/// against one deployment and an invoice names a seller in law. A single configured identity would
/// have every tenant issuing documents under one company's legal name, address and tax registration
/// — which is not a presentation defect but a false statement on a financial record.
/// <para>
/// The counterpart of <see cref="SubscriptionBillingProfile"/>: that one says who is buying, this one
/// says who is selling, and both are snapshotted onto every document so neither can be rewritten
/// after the fact. Configuration remains as the fallback for an installation that has not filled this
/// in yet, so nothing that worked before this existed stops working.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionMerchantProfile
{
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>The registered name of the selling entity, as it must appear on an invoice.</summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>The trading name, where it differs from the registered one.</summary>
    public string? DisplayName { get; set; }

    public BillingAddress? Address { get; set; }

    /// <summary>The seller's own VAT or tax registration, which many jurisdictions require.</summary>
    public string? TaxRegistrationId { get; set; }

    public string? SupportEmail { get; set; }

    /// <summary>Free text printed under the totals: bank details, terms, a remittance reference.</summary>
    public string? PaymentInstructions { get; set; }

    /// <summary>
    /// The storage id of an uploaded logo, or null for the text-only letterhead.
    /// </summary>
    /// <remarks>
    /// A storage id, never a URL — the same reason every other party on a document is copied rather
    /// than referenced. A file can be deleted, re-uploaded under the same name, or moved to a
    /// different bucket; an id resolved fresh at render time follows all three, which is exactly the
    /// drift a financial record cannot have. See <c>FinancialDocumentMerchant.LogoFileId</c> for the
    /// snapshot this is copied onto at issue.
    /// </remarks>
    public string? LogoFileId { get; set; }

    /// <summary>Normalized six-digit hex, e.g. <c>#17365D</c>. Null uses the shared default.</summary>
    public string? PrimaryColor { get; set; }

    /// <summary>Normalized six-digit hex. Null uses the shared default.</summary>
    public string? AccentColor { get; set; }

    /// <summary>
    /// Whether this is enough to issue a document under.
    /// </summary>
    /// <remarks>
    /// The legal name alone, deliberately. An address and a tax registration are required in some
    /// jurisdictions and meaningless in others, so demanding them here would be this module inventing
    /// a tax rule; a document with no seller named is wrong everywhere.
    /// </remarks>
    public bool IsComplete() => !string.IsNullOrWhiteSpace(LegalName);

    public string? LastUpdatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;

    public int Version { get; set; }
}
