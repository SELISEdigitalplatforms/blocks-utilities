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
