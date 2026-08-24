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

}
