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

    /// <summary>Which quantity item this multiplies. Null is a flat fee.</summary>
    public string? QuantityItemKey { get; set; }

    /// <summary>Tax to add on top, in basis points out of 10,000. Null means not taxable.</summary>
    public int? TaxRateBasisPoints { get; set; }
}
