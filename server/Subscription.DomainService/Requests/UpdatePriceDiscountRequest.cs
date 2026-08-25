using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

/// <summary>
/// Changes what a price takes off automatically, leaving its amount and cadence alone.
/// </summary>
/// <remarks>
/// A price's commercial terms are immutable once it exists, because every subscription sold on it
/// references them. This is the deliberate exception, alongside tax: it reaches future snapshots and
/// future moves onto the price only, and nobody already subscribed is repriced by it.
/// </remarks>
public sealed class UpdatePriceDiscountRequest
{
    /// <summary>
    /// The organization whose plan this price belongs to. Ignored unless the caller is the console —
    /// everyone else edits their own organization's prices, whatever this says.
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Basis points off, out of 10,000. Zero or null clears the automatic discount.
    /// </summary>
    public int? AutomaticDiscountBasisPoints { get; set; }

    /// <summary>
    /// How that discount meets a volume band. Ignored when the discount is being cleared, since
    /// there is then nothing for it to combine with.
    /// </summary>
    public AutomaticDiscountCombination? QuantityDiscountCombination { get; set; }
}
