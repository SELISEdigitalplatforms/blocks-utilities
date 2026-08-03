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
    Cancelled = 5
}
