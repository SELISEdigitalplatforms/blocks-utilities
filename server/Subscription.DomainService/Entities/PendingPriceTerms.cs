using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A discount or tax edit an operator chose to extend to a subscriber already on the price, waiting
/// for the renewal it was announced for.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="PendingPlanChange"/> on purpose. A pending plan change blocks
/// quantity changes, and this is held for a notice period the subscriber did not ask for — seats
/// must stay changeable through it. It also announces nothing at the boundary: the subscriber was
/// already told when it was scheduled, and "your plan changed from X to X" would be noise.
/// <para>
/// Frozen like every other scheduled change: the terms are copied here when the operator acts, so a
/// later edit nobody extended to existing subscribers cannot leak into a renewal through this.
/// </para>
/// <para>
/// Applied only while the subscription is still on <see cref="PriceId"/>. A subscriber who moved to
/// another price in the meantime was quoted that price's terms, and those win; the pending record is
/// simply dropped at the next renewal.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PendingPriceTerms
{
    /// <summary>The price these terms belong to.</summary>
    public string PriceId { get; set; } = string.Empty;

    /// <summary>The complete terms to install, not only the part that was edited.</summary>
    public PriceTerms Terms { get; set; } = new();

    /// <summary>
    /// The renewal boundary these take effect at: the first one at least the configured notice
    /// period after the operator acted.
    /// </summary>
    public DateTime EffectiveAtUtc { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    /// <summary>Who extended the edit, by name, for the audit trail and the notice email.</summary>
    public string? RequestedByName { get; set; }
}
