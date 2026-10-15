using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// What a credit purchase costs: price times quantity, less any code, plus tax.
/// </summary>
/// <remarks>
/// Pure and static, like the subscription amount calculator, and the only place the figure is
/// worked out, so a preview and the purchase that follows it cannot arrive at two different
/// totals. Discount and tax reuse that calculator's own rules — a percentage rounds the way a
/// subscription's does, and tax is applied once to the discounted amount — so a credit invoice
/// reads the same way every other invoice does.
/// </remarks>
public static class CreditQuoteCalculator
{
    /// <param name="discount">
    /// The code's terms, already checked as applicable. Null for no code.
    /// </param>
    /// <exception cref="OverflowException">
    /// When quantity times price does not fit. Bounded in practice by the meter's maximum purchase,
    /// and thrown rather than wrapped so a misconfigured price can never produce a negative charge.
    /// </exception>
    public static CreditQuote Quote(
        MeterTopUpPrice price,
        MeterTopUpTerms terms,
        decimal quantity,
        DiscountTerms? discount,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(terms);

        var gross = checked((long)quantity * price.UnitAmountMinor);

        // Zero periods applied: a purchase is one charge, so a code's duration never comes into it.
        var discounted = SubscriptionAmountCalculator.ApplyDiscount(gross, discount, 0, nowUtc);
        var tax = SubscriptionAmountCalculator.TaxBreakdownFor(
            discounted.AmountMinor,
            terms.TaxRateBasisPoints,
            terms.TaxMode);

        return new CreditQuote(
            price.UnitAmountMinor,
            gross,
            gross - discounted.AmountMinor,
            tax.NetAmountMinor,
            tax.TaxAmountMinor,
            tax.TotalAmountMinor,
            terms.TaxRateBasisPoints > 0 ? terms.TaxRateBasisPoints : null,
            terms.TaxRateBasisPoints > 0 ? terms.TaxMode ?? TaxMode.Exclusive : null);
    }
}

/// <summary>
/// One credit purchase's figures. <see cref="TotalMinor"/> is <see cref="NetMinor"/> plus
/// <see cref="TaxMinor"/> by construction, and <see cref="NetMinor"/> plus the tax inside it is
/// <see cref="GrossMinor"/> less <see cref="DiscountMinor"/>.
/// </summary>
public readonly record struct CreditQuote(
    long UnitAmountMinor,
    long GrossMinor,
    long DiscountMinor,
    long NetMinor,
    long TaxMinor,
    long TotalMinor,
    int? TaxRateBasisPoints,
    TaxMode? TaxMode);
