namespace Subscription.DomainService.Utilities;

public static class SubscriptionConstants
{
    /// <summary>
    /// Where subscription domain events are published. Nothing in this repository consumes it:
    /// the platform states what happened and each product decides what that means, which is why
    /// a quota alert is an event here rather than an email.
    /// </summary>
    public const string LifecycleTopic =
        "blocks_subscription_lifecycle_topic";

    public const string SubscriptionCreated = "SubscriptionCreated";
    public const string SubscriptionTrialStarted =
        "SubscriptionTrialStarted";
    public const string SubscriptionActivated =
        "SubscriptionActivated";
    public const string SubscriptionActivationFailed =
        "SubscriptionActivationFailed";
    public const string SubscriptionCancellationRequested =
        "SubscriptionCancellationRequested";
    public const string SubscriptionCanceled = "SubscriptionCanceled";
    public const string UsageThresholdReached = "UsageThresholdReached";

    /// <summary>
    /// Prefix for the order id a subscription's charges carry. Derived from the subscription id
    /// rather than stored separately, so a payment can be found again after a crash between
    /// starting the charge and recording the link to it.
    /// </summary>
    public const string OrderIdPrefix = "sub:";

    public static string OrderIdFor(string subscriptionId) =>
        $"{OrderIdPrefix}{subscriptionId}";

    /// <summary>
    /// The idempotency key a subscription's first charge is raised under.
    /// </summary>
    /// <remarks>
    /// Derived rather than random, and derived in one place so the checkout that writes it and
    /// the recovery sweep that looks it up cannot disagree. It is what lets a charge be found
    /// again after a crash between raising it and recording the link to it.
    /// </remarks>
    public static string InitialChargeKeyFor(string subscriptionId) =>
        $"sub-init:{subscriptionId}";
}
