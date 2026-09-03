using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// An organization's standing with one payment provider.
/// </summary>
/// <remarks>
/// The record the payment module never had: a provider's customer id survives there only on a
/// saved card, which cannot carry a subscription and disappears when the card is removed.
/// <para>
/// <see cref="ProviderCustomerId"/> is filled in when the first charge confirms rather than
/// created up front. Hosted checkout creates its own customer, so pre-creating one we cannot
/// pin to the session would leave an orphan behind on every signup.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class BillingAccount
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    /// <summary>The provider's identifier for this customer. Null until the first charge confirms.</summary>
    public string? ProviderCustomerId { get; set; }

    public string? BillingEmail { get; set; }

    public string? BillingName { get; set; }

    /// <summary>The saved method a renewal would charge.</summary>
    public string? DefaultPaymentMethodId { get; set; }

    /// <summary>
    /// The organization whose merchant configuration holds the card — not the subscriber's.
    /// </summary>
    /// <remarks>
    /// Organizations here are subscribers, not merchants: a tenant configures one payment
    /// provider and every organization's subscription is charged through it. So the scope that
    /// resolves the provider and the saved card is the one that took the money at signup, which
    /// is rarely the organization being billed — a console-created subscription belongs to the
    /// customer while the charge ran under the console's own.
    /// <para>
    /// Recorded from the initial payment rather than read from configuration, so it cannot drift
    /// from where the card actually lives and stays correct whether the provider is registered
    /// for one organization or for the whole tenant. Null on accounts created before this was
    /// recorded; those fall back to the subscriber's organization, which is what they used.
    /// </para>
    /// </remarks>
    public string? ProviderOrganizationId { get; set; }

    /// <summary>
    /// The item id of the exact <c>Payment.DomainService.Entities.PaymentProvider</c> row that
    /// readiness resolved and this account was pinned to at signup.
    /// </summary>
    /// <remarks>
    /// This, not <see cref="ProviderOrganizationId"/>, is what checkout should compare a payment's
    /// resolved provider against: two different organizations can each hold their own
    /// configuration for the same provider name, and only the row's own identity -- never an
    /// organization id, which a tenant-level configuration answers under several -- says whether a
    /// charge actually went through the configuration this account was pinned to. Null on
    /// accounts created before this was recorded; the checkout comparison that reads it skips
    /// itself rather than failing closed against a fact those accounts never captured.
    /// </remarks>
    public string? ProviderId { get; set; }

    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
