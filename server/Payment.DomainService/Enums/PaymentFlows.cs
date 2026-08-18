namespace Payment.DomainService.Enums;

public static class PaymentFlows
{
    public const string HostedCheckout = "HOSTED_CHECKOUT";

    public const string RecurringCharge = "RECURRING_CHARGE";

    /// <summary>
    /// A subscription period settled as a provider invoice rather than a bare charge.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="RecurringCharge"/> because the money arrives already captured and
    /// with an invoice document behind it: there is no authorize-then-capture step to reserve a
    /// lease for, and the record exists so the charge can be reconciled and its invoice fetched,
    /// not so anything can be driven from it.
    /// </remarks>
    public const string SubscriptionInvoice = "SUBSCRIPTION_INVOICE";

    public static readonly string[] All =
    [
        HostedCheckout,
        RecurringCharge,
        SubscriptionInvoice
    ];
}
