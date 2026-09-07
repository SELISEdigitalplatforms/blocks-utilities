using Subscription.DomainService.Entities;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// The shared shape of quantities, bands and scheduled changes on the wire.
/// </summary>
/// <remarks>
/// One place because two surfaces describe the same things: the response to a quantity change and
/// the ordinary subscription read. Two copies of this mapping is how a scheduled decrease comes to
/// look like one change on the write and a different one on the next read.
/// </remarks>
internal static class QuantityResponseMapper
{
    public static QuantityChangeItemResponse Item(SubscriptionQuantityItem item) => new()
    {
        ItemKey = item.ItemKey,
        UnitLabel = item.UnitLabel,
        Quantity = item.Quantity
    };

    /// <summary>
    /// A held quantity described alongside the bounds the plan sells it in.
    /// </summary>
    /// <remarks>
    /// <paramref name="planItems"/> comes from the plan the quantity is priced against -- the
    /// subscription's own snapshot for what it holds today, the target plan for a change being
    /// previewed or scheduled. An item with no match there is a snapshot written before the plan
    /// carried bounds for it; all such an item can honestly say is what it holds.
    /// </remarks>
    public static SubscriptionQuantityResponse Subscription(
        SubscriptionQuantityItem item,
        IReadOnlyCollection<PlanQuantityItem>? planItems)
    {
        var planItem = planItems?.FirstOrDefault(
            p => string.Equals(p.ItemKey, item.ItemKey, StringComparison.OrdinalIgnoreCase));

        return new SubscriptionQuantityResponse
        {
            ItemKey = item.ItemKey,
            UnitLabel = item.UnitLabel,
            Quantity = item.Quantity,
            MinQuantity = planItem?.MinQuantity ?? 1,
            MaxQuantity = planItem?.MaxQuantity,
            DefaultQuantity = planItem?.DefaultQuantity ?? item.Quantity
        };
    }

    public static QuantityDiscountTierResponse? Tier(QuantityDiscountTier? tier) =>
        tier is null
            ? null
            : new QuantityDiscountTierResponse
            {
                MinimumQuantity = tier.MinimumQuantity,
                MaximumQuantity = tier.MaximumQuantity,
                DiscountBasisPoints = tier.DiscountBasisPoints
            };

    public static PendingQuantityChangeResponse? Pending(PendingQuantityChange? pending) =>
        pending is null
            ? null
            : new PendingQuantityChangeResponse
            {
                Quantities = pending.RequestedQuantities.Select(Item).ToList(),
                RequestedAtUtc = pending.RequestedAtUtc,
                EffectiveAtUtc = pending.EffectiveAtUtc
            };
}
