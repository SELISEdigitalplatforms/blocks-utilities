using Subscription.DomainService.Entities;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Services;

/// <summary>
/// Matches requested quantities to a plan's items, snapshotting each at the price it was bought
/// at. Shared by subscribing and by changing plan — both are "pick a plan and some quantities,"
/// and the bounds-checking has to agree in both places or a plan change could grant what
/// signing up would have refused.
/// </summary>
internal static class SubscriptionQuantityBuilder
{
    /// <summary>
    /// Fills in defaults for anything the caller left out and refuses anything outside the
    /// plan's bounds or naming an item the plan does not have.
    /// </summary>
    public static List<SubscriptionQuantityItem>? Build(
        IReadOnlyList<SubscriptionQuantityRequest> requested,
        Plan plan,
        Price price)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(price);

        var requestedByKey = requested.ToDictionary(
            quantity => quantity.ItemKey,
            quantity => quantity.Quantity,
            StringComparer.Ordinal);

        if (requestedByKey.Keys.Any(key =>
                !plan.QuantityItems.Exists(item =>
                    string.Equals(item.ItemKey, key, StringComparison.Ordinal))))
        {
            return null;
        }

        var items = new List<SubscriptionQuantityItem>(plan.QuantityItems.Count);

        foreach (var item in plan.QuantityItems)
        {
            var quantity = requestedByKey.TryGetValue(item.ItemKey, out var supplied)
                ? supplied
                : item.DefaultQuantity;

            if (quantity < item.MinQuantity ||
                (item.MaxQuantity is { } maximum && quantity > maximum))
            {
                return null;
            }

            items.Add(new SubscriptionQuantityItem
            {
                ItemKey = item.ItemKey,
                UnitLabel = item.UnitLabel,
                Quantity = quantity,
                // Snapshotted so a later catalogue edit cannot move what this subscriber pays.
                UnitAmountMinor = string.Equals(
                    item.ItemKey,
                    price.QuantityItemKey,
                    StringComparison.Ordinal)
                    ? price.UnitAmountMinor
                    : 0
            });
        }

        return items;
    }
}
