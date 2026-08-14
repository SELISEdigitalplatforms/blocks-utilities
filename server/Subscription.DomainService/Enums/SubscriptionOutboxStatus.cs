namespace Subscription.DomainService.Enums;

/// <summary>
/// Where an outbox event has got to. Mirrors the payment outbox rather than sharing it: the two
/// lifecycles are independent and will diverge.
/// </summary>
public enum SubscriptionOutboxStatus
{
    Pending = 0,
    Processing = 1,
    Published = 2,
    RetryScheduled = 3,
    Abandoned = 4
}
