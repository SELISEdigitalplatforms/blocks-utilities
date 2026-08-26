using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// Who an organization's financial documents are addressed to.
/// </summary>
/// <remarks>
/// Separate from <see cref="BillingAccount"/>, which records an organization's standing with one
/// payment <em>provider</em> — a customer id, a saved card, the merchant scope that took the money.
/// This records the subscriber's identity as it must appear on a document: a legal name, an address,
/// a tax id. One organization can hold several billing accounts, one per provider, and they must all
/// print the same name.
/// <para>
/// Nothing here is ever read at document-render time. Every value is copied onto the document when it
/// is issued, because an invoice states who the customer was on its issue date and editing an address
/// afterwards must not rewrite last year's paperwork.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionBillingProfile
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>The subscribing organization. One profile each.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// The name the organization contracts under, which is what a document has to carry.
    /// </summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>
    /// What the organization calls itself in a product surface, when that differs. Falls back to
    /// <see cref="LegalName"/> rather than being required twice.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>Where a document is sent, and the name it is addressed to.</summary>
    public string BillingContactName { get; set; } = string.Empty;

    public string BillingContactEmail { get; set; } = string.Empty;

    public BillingAddress? Address { get; set; }

    /// <summary>
    /// A VAT or other tax registration number, unvalidated and unformatted.
    /// </summary>
    /// <remarks>
    /// Held as the subscriber typed it. Every jurisdiction spells these differently and a
    /// normalisation this module invented would print something the subscriber's own accountant
    /// does not recognise.
    /// </remarks>
    public string? TaxRegistrationId { get; set; }

    /// <summary>
    /// Per-user names and addresses, so a document can say who asked for the change.
    /// </summary>
    /// <remarks>
    /// Keyed by user id and filled in as users act, rather than synchronised from the identity
    /// provider. A document has to name the person who initiated it as they were <em>then</em>, and
    /// an identity directory answers only about now — people leave, and rename.
    /// </remarks>
    public List<BillingContact> Contacts { get; set; } = [];

    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this profile carries everything a document must state about its recipient.
    /// </summary>
    /// <remarks>
    /// The address and the tax id are deliberately not part of it. A great many subscribers are
    /// individuals with neither, and refusing them a subscription over a field their jurisdiction
    /// does not ask for would be a billing rule invented here.
    /// </remarks>
    public bool IsComplete() =>
        !string.IsNullOrWhiteSpace(LegalName) &&
        !string.IsNullOrWhiteSpace(BillingContactName) &&
        !string.IsNullOrWhiteSpace(BillingContactEmail);
}

/// <summary>A postal address, held as lines because addresses are not a fixed shape.</summary>
[BsonIgnoreExtraElements]
public sealed class BillingAddress
{
    public string? Line1 { get; set; }

    public string? Line2 { get; set; }

    public string? City { get; set; }

    /// <summary>State, province or canton, where the jurisdiction has one.</summary>
    public string? Region { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? CountryCode { get; set; }

    public bool IsEmpty() =>
        string.IsNullOrWhiteSpace(Line1) &&
        string.IsNullOrWhiteSpace(Line2) &&
        string.IsNullOrWhiteSpace(City) &&
        string.IsNullOrWhiteSpace(Region) &&
        string.IsNullOrWhiteSpace(PostalCode) &&
        string.IsNullOrWhiteSpace(CountryCode);
}

/// <summary>One person who may initiate a billing change on the organization's behalf.</summary>
[BsonIgnoreExtraElements]
public sealed class BillingContact
{
    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
