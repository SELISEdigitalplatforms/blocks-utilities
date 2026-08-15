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

    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
