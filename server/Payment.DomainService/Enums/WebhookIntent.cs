namespace Payment.DomainService.Enums;

/// <summary>
/// What an inbound webhook is asking the system to do, once its provider-specific event name
/// has been translated. State transitions dispatch on this rather than on provider event codes.
/// </summary>
public enum WebhookIntent
{
    /// <summary>Recognised but not acted on.</summary>
    Ignored = 0,
    Authorization = 1,
    Refund = 2,
    Capture = 3,
    StoredMethod = 4,
    Cancelled = 5,

    /// <summary>
    /// A card was collected without being charged, or the attempt to collect one ended.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Authorization"/> because the authorisation path proves the event
    /// against the payment's amount and currency, and a setup has neither. Routed the same way:
    /// the reference this service minted is echoed back on the object.
    /// </remarks>
    PaymentMethodSetup = 6
}
