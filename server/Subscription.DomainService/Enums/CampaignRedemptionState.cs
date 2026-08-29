namespace Subscription.DomainService.Enums;

/// <summary>
/// Where one subscription's claim on a campaign stands.
/// </summary>
/// <remarks>
/// Explicit values because this is persisted, the same reason <see cref="SubscriptionStatus"/>'s
/// are: inserting a member without one would renumber every value after it and silently
/// reinterpret a stored document.
/// <para>
/// <see cref="Released"/> must stay the highest ordinal.
/// <see cref="Repositories.CampaignRedemptionIndexDefinitions"/>'s partial unique index expresses
/// "not Released" as <c>State &lt; Released</c>, because MongoDB's partial-filter expressions do
/// not support <c>$ne</c> or <c>$in</c> — only equality and ordered comparisons. Adding a state
/// after <see cref="Released"/> rather than before it would silently break that index's meaning
/// without changing a line in this file.
/// </para>
/// </remarks>
public enum CampaignRedemptionState
{
    /// <summary>Claimed at subscription creation. Grants the discount, consumes nothing durable yet.</summary>
    Reserved = 0,

    /// <summary>The subscription activated. The claim is now permanent.</summary>
    Redeemed = 1,

    /// <summary>
    /// The subscription ended before ever activating, and this claim is queued to be given back.
    /// Distinct from <see cref="Released"/> so a crash between the two is visible rather than
    /// silently either state.
    /// </summary>
    ReleasePending = 2,

    /// <summary>
    /// Given back. A one-use campaign's slot is free for a different organization again. Must
    /// remain the highest ordinal -- see the remarks above.
    /// </summary>
    Released = 3
}
