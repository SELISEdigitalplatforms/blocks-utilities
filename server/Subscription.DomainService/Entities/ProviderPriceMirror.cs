using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The provider's own identifiers for a price we mirrored to it.
/// </summary>
/// <remarks>
/// Mirroring exists so a later invoice can name a real provider price. It deliberately does not
/// create a provider-side subscription: the billing clock is ours, and a provider running its
/// own would bill the same period twice.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class ProviderPriceMirror
{
    public string ProviderName { get; set; } = string.Empty;

    public string? ProviderProductId { get; set; }

    public string? ProviderPriceId { get; set; }

    public DateTime MirroredAtUtc { get; set; } = DateTime.UtcNow;
}
