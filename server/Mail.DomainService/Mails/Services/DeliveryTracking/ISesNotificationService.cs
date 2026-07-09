namespace Mail.DomainService.Mails.Services.DeliveryTracking;

public interface ISesNotificationService
{
    Task<SesNotificationResult> ProcessAsync(string payloadJson, CancellationToken cancellationToken = default);
}

public enum SesNotificationOutcome
{
    Processed,
    Duplicate,
    SubscriptionConfirmed,
    UnsubscribeAcknowledged,
    Invalid,
    Forbidden
}

public sealed record SesNotificationResult(SesNotificationOutcome Outcome, string? Error = null)
{
    public static SesNotificationResult Processed() => new(SesNotificationOutcome.Processed);
    public static SesNotificationResult Duplicate() => new(SesNotificationOutcome.Duplicate);
    public static SesNotificationResult Invalid(string error) => new(SesNotificationOutcome.Invalid, error);
    public static SesNotificationResult Forbidden(string error) => new(SesNotificationOutcome.Forbidden, error);
}
