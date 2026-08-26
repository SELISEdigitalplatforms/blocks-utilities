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

    /// <summary>
    /// Collecting a card without charging it, so a later off-session charge has a mandate.
    /// </summary>
    /// <remarks>
    /// A payment record with no money in it. It exists because everything that tracks a hosted
    /// session — the initiation lease, the redirect URL, the webhook route, the stored-card
    /// write — already hangs off <see cref="Entities.PaymentDetail"/>, and a parallel entity
    /// would have to reimplement all of it to describe something simpler.
    /// <para>
    /// Deliberately absent from <see cref="All"/>. That list is what a caller may filter the
    /// payments endpoint by, and these are not payments: the amount is always zero, so anything
    /// that sums, refunds or reconciles them is reading a financial record that does not exist.
    /// Every such reader excludes this flow explicitly.
    /// </para>
    /// </remarks>
    public const string PaymentMethodSetup = "PAYMENT_METHOD_SETUP";

    public static readonly string[] All =
    [
        HostedCheckout,
        RecurringCharge,
        SubscriptionInvoice
    ];
}
