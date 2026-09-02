namespace Payment.DomainService.Utilities;

public static class PaymentConstants
{
    public const string AdyenOnlineProvider = "ADYEN-ONLINE";
    public const string StripeProvider = "STRIPE";

    /// <summary>
    /// Adyen's recurring model for a shopper-present token later charged on a fixed, merchant-set
    /// schedule with nobody present -- a subscription renewal, specifically -- as distinct from
    /// <see cref="AdyenCardOnFileRecurringModel"/>. See
    /// https://docs.adyen.com/online-payments/tokenization/make-token-payments.
    /// </summary>
    public const string SubscriptionRecurringModel = "Subscription";

    /// <summary>
    /// Adyen's recurring model for a token the shopper themselves initiates a later charge
    /// against (an on-demand top-up, a one-click repeat purchase) -- not a fixed schedule the
    /// merchant drives. The long-standing default for any token this module saves outside a
    /// subscription checkout.
    /// </summary>
    public const string AdyenCardOnFileRecurringModel = "CardOnFile";
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
    public const string PaymentCaptureRequested =
        "PaymentCaptureRequested";
    public const string PaymentCaptured = "PaymentCaptured";
    public const string PaymentCaptureFailed =
        "PaymentCaptureFailed";
    public const string PaymentCancelled = "PaymentCancelled";

    /// <summary>
    /// A card was stored without being charged, or the attempt to store one ended.
    /// </summary>
    /// <remarks>
    /// Named apart from <see cref="PaymentAuthorized"/> and <see cref="PaymentRefused"/> even
    /// though the underlying record moves through the same statuses. Subscribers act on the
    /// event name, and one that says a payment was authorised would have them recording money
    /// that nobody moved.
    /// </remarks>
    public const string PaymentMethodSetupSucceeded = "PaymentMethodSetupSucceeded";

    public const string PaymentMethodSetupFailed = "PaymentMethodSetupFailed";
}
