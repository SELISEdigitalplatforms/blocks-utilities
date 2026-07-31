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

    /// <summary>The merchant the object belongs to, checked on every inbound event.</summary>
    public const string MerchantAccountKey = "merchant_account";

    /// <summary>
    /// Which organization within the tenant owns the object. Echoed back on every event and
    /// checked against the payment, so an event cannot be attributed to the wrong business.
    /// </summary>
    public const string OrganizationKey = "organization_id";

    /// <summary>
    /// Metadata for an object created against an existing payment — a refund, say — where the
    /// reference identifies that operation rather than the payment.
    /// </summary>
    /// <remarks>
    /// The merchant account is not part of the request Stripe needs, since the API key already
    /// identifies the account, but intake verifies every event against the merchant recorded
    /// on the payment. Omitting it here left the check comparing against nothing, so the
    /// delivery was rejected as unauthorized and retried forever.
    /// </remarks>
    public static Dictionary<string, string?> ForOperation(
        string reference,
        string merchantAccount) =>
        new(StringComparer.Ordinal)
        {
            [ReferenceKey] = reference,
            [MerchantAccountKey] = merchantAccount
        };
}
