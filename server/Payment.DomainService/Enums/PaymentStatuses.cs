namespace Payment.DomainService.Enums;

public static class PaymentStatuses
{
    public const string Initiating = "INITIATING";
    public const string Processing = "PROCESSING";
    public const string MakePaymentFailed = "MAKE_PAYMENT_FAILED";
    public const string InitiationUnknown = "INITIATION_UNKNOWN";
    public const string Authorized = "AUTHORIZED";
    public const string Refused = "REFUSED";
    public const string PartiallyCaptured = "PARTIALLY_CAPTURED";
    public const string Captured = "CAPTURED";
    public const string Cancelled = "CANCELLED";
    public const string PartiallyRefunded = "PARTIALLY_REFUNDED";
    public const string Refunded = "REFUNDED";

    /// <summary>
    /// A card setup that timed out with one of its two completion signals (see
    /// <c>PaymentMethodSetupWebhookStateTransitionService</c>) never arriving. Terminal for the
    /// idempotency key that reserved it, the same way
    /// <see cref="Refused"/>/<see cref="Cancelled"/>/<see cref="MakePaymentFailed"/> are: a fresh
    /// attempt needs a fresh key rather than resuming a session that will never complete.
    /// </summary>
    public const string Expired = "EXPIRED";

    public static readonly string[] All =
    [
        Initiating,
        Processing,
        MakePaymentFailed,
        InitiationUnknown,
        Authorized,
        Refused,
        PartiallyCaptured,
        Captured,
        Cancelled,
        PartiallyRefunded,
        Refunded,
        Expired
    ];
}
