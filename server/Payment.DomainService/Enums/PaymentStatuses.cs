namespace Payment.DomainService.Enums;

public static class PaymentStatuses
{
    public const string Initiating = "INITIATING";
    public const string Processing = "PROCESSING";
    public const string MakePaymentFailed = "MAKE_PAYMENT_FAILED";
    public const string InitiationUnknown = "INITIATION_UNKNOWN";
    public const string Authorized = "AUTHORIZED";
    public const string Refused = "REFUSED";
}
