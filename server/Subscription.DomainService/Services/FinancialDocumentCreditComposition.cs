using Payment.DomainService.Entities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// What a banked credit is made of: how much of it was net value and how much was tax.
/// </summary>
/// <remarks>
/// A downgrade returns unused time on the plan being left. That value was charged with tax and with
/// discounts applied, so returning it returns a proportion of each — and a credit note that reported
/// the whole figure as untaxed net would leave the subscriber unable to reverse the tax they had
/// already reclaimed.
/// <para>
/// Composed at the change rather than at issue, because the outgoing side's rate and mode are gone
/// from the subscription the moment the new plan replaces them. This runs while both are still in
/// hand and freezes the answer.
/// </para>
/// </remarks>
public static class FinancialDocumentCreditComposition
{
    /// <summary>
    /// Decomposes a banked credit using the outgoing side of the settlement it came from.
    /// </summary>
    /// <param name="outgoingPrice">
    /// The price being left, for its tax rate and mode — the settlement records amounts but not the
    /// rate they were computed at, and the target price's rate is the wrong one: a change can cross
    /// between inclusive and exclusive tax.
    /// </param>
    /// <param name="settlement">
    /// The two-sided proration. When absent the credit is reported as a single net figure, which is
    /// the honest answer: with no breakdown to allocate from, inventing a tax split would be stating
    /// a tax reversal nobody computed.
    /// </param>
    /// <param name="creditedMinor">What was banked, as a positive figure, tax included.</param>
    public static FinancialDocumentAmounts ForBankedCredit(
        PriceSnapshot outgoingPrice,
        SubscriptionSettlementBreakdown? settlement,
        long creditedMinor)
    {
        ArgumentNullException.ThrowIfNull(outgoingPrice);

        var taxRate = outgoingPrice.TaxRateBasisPoints;
        var taxMode = SubscriptionTaxPresentation.Describe(outgoingPrice);

        if (settlement?.Outgoing is not { PeriodTotalMinor: > 0 } outgoing ||
            creditedMinor <= 0)
        {
            return new FinancialDocumentAmounts
            {
                GrossSubtotalMinor = creditedMinor,
                NetSubtotalMinor = creditedMinor,
                TotalMinor = creditedMinor,
                TaxRateBasisPoints = taxRate,
                TaxMode = taxMode
            };
        }

        // The outgoing side priced as a whole period, which is the shape a partial reversal allocates
        // from. Reusing the refund allocator rather than writing a second one on purpose: a credit
        // note for unused time and a credit note for a partial refund are the same arithmetic — return
        // a proportion of a charge and keep every figure adding up — and two implementations would be
        // two roundings.
        var basis = new FinancialDocumentAmounts
        {
            GrossSubtotalMinor = outgoing.GrossAmountMinor,
            // The built-in reduction, whole. The settlement records the combined figure without the
            // two rates that produced it, so splitting it back into automatic and quantity here would
            // be a guess; reporting it as the automatic discount is coarse but true, and it keeps
            // gross less discounts equal to net.
            AutomaticDiscountMinor = outgoing.BuiltInDiscountMinor,
            PromotionalDiscountMinor = outgoing.PromotionalDiscountMinor,
            NetSubtotalMinor = outgoing.PeriodTotalMinor - outgoing.TaxAmountMinor,
            TaxRateBasisPoints = taxRate,
            TaxMode = taxMode,
            TaxAmountMinor = outgoing.TaxAmountMinor,
            TotalMinor = outgoing.PeriodTotalMinor
        };

        return SubscriptionFinancialDocumentIssuer.ReverseProportionally(basis, creditedMinor);
    }
}
