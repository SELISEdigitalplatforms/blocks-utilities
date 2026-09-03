using Payment.DomainService.Entities;

namespace Payment.DomainService.Requests;

public sealed class CreateRecurringPaymentRequest
{
    public string ProviderName { get; set; } = "ADYEN-ONLINE";

    public string StoredPaymentMethodId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public string RecurringProcessingModel { get; set; } = "Subscription";

    public string? Description { get; set; }

    /// <summary>
    /// The subscription invoice breakdown this charge is made of, mirroring
    /// <see cref="PaymentDetail"/>'s own <c>Subscription*</c> fields -- set only by a subscription
    /// renewal, dunning retry, plan-change or quantity-change settlement, or usage invoice, all of
    /// which compose this charge through <c>RecurringChargeBillingGateway</c> rather than a
    /// provider-specific invoicing gateway. Null for an ordinary unscheduled-card-on-file charge,
    /// which is not composing a subscription invoice at all.
    /// </summary>
    /// <remarks>
    /// Lives here, on the generic recurring-payment request, rather than on some
    /// subscription-only variant, because every non-Stripe-invoice provider -- Adyen included --
    /// is charged through this one shared path. Without it, a subscription charged through any
    /// provider other than Stripe's own Invoice API would record a payment with none of the
    /// figures its own invoice is built from.
    /// </remarks>
    public SubscriptionInvoiceBreakdown? SubscriptionInvoiceBreakdown { get; set; }
}

/// <summary>
/// The flat gross/discount/tax/credit breakdown behind one subscription charge -- see
/// <see cref="PaymentDetail"/>'s matching <c>Subscription*</c> fields for what each means.
/// </summary>
/// <remarks>
/// Distinct from <see cref="SubscriptionSettlementBreakdown"/>: that describes a plan or quantity
/// change's amount as a subtraction between two prorated periods, while this describes an ordinary
/// discounted price. A charge carries at most one of the two, matching <c>PaymentDetail</c> itself.
/// </remarks>
public sealed class SubscriptionInvoiceBreakdown
{
    public long NetAmountMinor { get; set; }

    public long TaxAmountMinor { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    /// <summary>"Exclusive" or "Inclusive". Null when the price carries no tax.</summary>
    public string? TaxMode { get; set; }

    public long CreditConsumedMinor { get; set; }

    /// <summary>
    /// The charge before any reduction. Zero here reads the same way it does on
    /// <see cref="PaymentDetail.SubscriptionGrossAmountMinor"/> -- as "no breakdown was composed
    /// for this charge" -- so the three fields below are recorded only alongside a positive gross.
    /// </summary>
    public long GrossAmountMinor { get; set; }

    public long BuiltInDiscountMinor { get; set; }

    public long PromotionalDiscountMinor { get; set; }

    public int? AutomaticDiscountBasisPoints { get; set; }

    public int? QuantityDiscountBasisPoints { get; set; }

    public string? DiscountCombination { get; set; }

    /// <summary>
    /// Set instead of the flat fields above when this charge settles a plan or quantity change.
    /// </summary>
    public SubscriptionSettlementBreakdown? Settlement { get; set; }
}
