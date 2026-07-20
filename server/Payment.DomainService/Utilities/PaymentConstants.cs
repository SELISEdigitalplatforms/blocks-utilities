namespace Payment.DomainService.Utilities;

public static class PaymentConstants
{
    public const string AdyenOnlineProvider = "ADYEN-ONLINE";
    public const string LifecycleTopic = "blocks_payment_lifecycle_topic";
    public const string PaymentWorkQueue =
        "blocks_payment_work_listener";
    public const string PaymentInitiated = "PaymentInitiated";
    public const string PaymentInitiationFailed = "PaymentInitiationFailed";
    public const string PaymentAuthorized = "PaymentAuthorized";
    public const string PaymentRefused = "PaymentRefused";
    public const string PaymentRefundRequested =
        "PaymentRefundRequested";
    public const string PaymentRefundSucceeded =
        "PaymentRefundSucceeded";
    public const string PaymentRefundFailed =
        "PaymentRefundFailed";
    public const string PaymentRefundReversed =
        "PaymentRefundReversed";
}
