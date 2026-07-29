namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// The metadata key Stripe objects carry so their events can be routed back to the record
/// that created them.
/// </summary>
/// <remarks>
/// Stripe never echoes a caller-supplied reference field on refunds the way Adyen echoes
/// <c>merchantReference</c>, so the reference has to be carried as metadata. The key is the
/// same one the checkout session and payment intent use, because the webhook normalizer reads
/// exactly one key to route any object.
/// </remarks>
public static class StripeRoutingMetadata
{
    public const string ReferenceKey = "tenant_reference";

    /// <summary>
    /// Identifies which shopper owns a card saved during this payment. Must be the reference
    /// this service derived, not Stripe's customer id — the two are different identifiers, and
    /// storing a card checks the echoed value against the one recorded on the payment.
    /// </summary>
    public const string ShopperReferenceKey = "shopper_reference";

    /// <summary>
    /// Metadata for an object created against an existing payment — a refund, say — where the
    /// reference identifies that operation rather than the payment.
    /// </summary>
    public static Dictionary<string, string?> ForOperation(string reference) =>
        new(StringComparer.Ordinal)
        {
            [ReferenceKey] = reference
        };
}
