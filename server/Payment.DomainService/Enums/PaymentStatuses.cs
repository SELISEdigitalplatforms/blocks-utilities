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
}
