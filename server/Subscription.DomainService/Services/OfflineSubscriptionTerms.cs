using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// Turns a freshly built subscription into an offline one. See spec 006.
/// </summary>
/// <remarks>
/// Pure and static so the rule is testable without the creation service around it.
/// <para>
/// A contract amount is written into the subscription's own <see cref="PriceSnapshot"/> rather
/// than kept beside it and consulted by every calculator. The snapshot already is "the price this
/// subscriber agreed to, frozen against catalogue edits", which is exactly what a contract is —
/// so renewal, proration and preview price it correctly without a branch of their own.
/// </para>
/// </remarks>
public static class OfflineSubscriptionTerms
{
    public static void Apply(SubscriptionDetail subscription, OfflinePricing pricing)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(pricing);

        subscription.CollectionMethod = SubscriptionCollectionMethod.OfflineInvoice;
        subscription.OfflinePricing = new OfflinePricing
        {
            Mode = pricing.Mode,
            ContractAmountMinor = pricing.ContractAmountMinor,
            ContractCurrencyCode = pricing.ContractCurrencyCode?.Trim().ToUpperInvariant()
        };

        // The operator activated it; nobody bought it. See SubscriptionDetail.ActivatedBy.
        subscription.ActivatedBy = subscription.PurchasedBy;
        subscription.PurchasedBy = null;

        if (pricing.Mode != OfflinePricingMode.Contract)
        {
            return;
        }

        var currency = subscription.OfflinePricing.ContractCurrencyCode!;
        var price = subscription.Price;

        subscription.CurrencyCode = currency;
        price.CurrencyCode = currency;

        // One flat amount whatever the quantities: the price stops charging per unit, and every
        // unit stops carrying a price of its own.
        price.UnitAmountMinor = pricing.ContractAmountMinor!.Value;
        price.QuantityItemKey = null;

        // Net: a negotiated figure already includes whatever the operator meant to give. The
        // price's cadence discount and the plan's volume bands do not reduce it again. A discount
        // code the operator applied still does — it is on the subscription, not the price.
        price.AutomaticDiscountBasisPoints = null;
        price.QuantityDiscountCombination = null;

        foreach (var item in subscription.QuantityItems)
        {
            item.UnitAmountMinor = 0;
        }
    }
}
