using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

public sealed class CreatePriceRequest
{
    public string PlanId { get; set; } = string.Empty;

    /// <summary>
    /// The organization whose plan this price belongs to. Ignored unless the caller is the
    /// console (<c>Payment:ConsoleOrganizationId</c>) — everyone else prices their own
    /// organization's plans, whatever this says.
    /// </summary>
    public string? OrganizationId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>
    /// The amount in minor units — 8900 is CHF 89.00. Never a decimal here: the exponent
    /// belongs to the currency, and a request carrying both invites them to disagree.
    /// </summary>
    public long UnitAmountMinor { get; set; }

    public BillingInterval Interval { get; set; } = BillingInterval.Month;

    /// <summary>Paired with the interval, so three months is a quarter and no enum grows.</summary>
    public int IntervalCount { get; set; } = 1;

    /// <summary>
    /// Where renewals land: on the subscriber's anniversary, or on the first of the month with a
    /// prorated opening period.
    /// </summary>
    /// <remarks>
    /// Only <c>Month</c> with an interval count of one may be calendar-aligned; anything else is
    /// refused as <c>subscription_billing_alignment_invalid</c>. Omitted means anniversary, so a
    /// caller that has never heard of alignment authors exactly the price it always did.
    /// </remarks>
    public BillingAlignment BillingAlignment { get; set; } = BillingAlignment.Anniversary;

    /// <summary>
    /// The monthly price a calendar-aligned yearly price prices its opening stub from.
    /// </summary>
    /// <remarks>
    /// Required when this price is <c>Year</c> × 1 and calendar-aligned; refused on every other
    /// price. It prices the opening stub only — <see cref="UnitAmountMinor"/> remains the authored
    /// annual amount, which is a commercial decision rather than twelve times anything.
    /// </remarks>
    public string? CalendarStubBasePriceId { get; set; }

    /// <summary>
    /// When a calendar-aligned yearly price collects its annual amount. Omitted means
    /// <c>AtBoundary</c>. Refused on any other price.
    /// </summary>
    public CalendarAnnualChargeTiming? CalendarAnnualChargeTiming { get; set; }

    public string? DisplayPriceNote { get; set; }

    /// <summary>Which quantity item this multiplies. Null is a flat fee.</summary>
    public string? QuantityItemKey { get; set; }

    /// <summary>Tax to add on top, in basis points out of 10,000. Null means not taxable.</summary>
    public int? TaxRateBasisPoints { get; set; }

    /// <summary>
    /// Whether <see cref="TaxRateBasisPoints"/> is added to <see cref="UnitAmountMinor"/> or already
    /// contained in it. Required whenever a positive rate is sent.
    /// </summary>
    /// <remarks>
    /// Required rather than defaulted, because the two readings of the same number differ by the tax
    /// itself: CHF 145 at 7.7% is either CHF 156.17 or CHF 145.00 to the customer, and guessing on
    /// the author's behalf is how a catalogue ends up mispriced by a rounding-shaped margin.
    /// </remarks>
    public TaxMode? TaxMode { get; set; }

    /// <summary>
    /// A percentage off this price, applied without a code, in basis points — 800 is 8%.
    /// Omitted or zero is no automatic discount.
    /// </summary>
    public int? AutomaticDiscountBasisPoints { get; set; }

    /// <summary>
    /// How that discount meets a volume band. Omitted reads as <c>BestDiscount</c>, which takes
    /// the larger of the two and never both — the answer that cannot give away more than the
    /// author realised.
    /// </summary>
    public AutomaticDiscountCombination? QuantityDiscountCombination { get; set; }
}
