namespace Subscription.DomainService.Enums;

/// <summary>
/// Where an offline subscription's recurring amount comes from. See spec 006.
/// </summary>
/// <remarks>Values are explicit because they are persisted.</remarks>
public enum OfflinePricingMode
{
    /// <summary>The chosen catalogue price, frozen exactly as a card subscription's is.</summary>
    Catalogue = 0,

    /// <summary>
    /// One flat, net amount per billing interval agreed in the contract, whatever the quantities.
    /// </summary>
    Contract = 1,

    /// <summary>
    /// Priced entirely outside Utilities. Nothing is invoiced; a service statement records each
    /// period instead.
    /// </summary>
    None = 2
}
