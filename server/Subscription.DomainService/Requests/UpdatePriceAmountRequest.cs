namespace Subscription.DomainService.Requests;

public sealed class UpdatePriceAmountRequest
{
    public string? OrganizationId { get; set; }

    /// <summary>The new amount, in minor units — per unit for a price that multiplies a quantity item.</summary>
    public long UnitAmountMinor { get; set; }

    /// <summary>
    /// Whether subscribers already on this price move to the new amount too, from their first renewal
    /// at least <c>Subscription:PriceTermsNoticeDays</c> away, each told by email. False by default:
    /// nobody already subscribed is repriced unless the operator asks.
    /// </summary>
    public bool ApplyToExistingSubscribers { get; set; }
}
