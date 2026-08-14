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
    public const string SubscriptionRenewed = "SubscriptionRenewed";
    public const string SubscriptionRenewalFailed = "SubscriptionRenewalFailed";
    public const string SubscriptionPastDue = "SubscriptionPastDue";
    public const string SubscriptionUnpaid = "SubscriptionUnpaid";
    public const string SubscriptionPlanChanged = "SubscriptionPlanChanged";

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

    /// <summary>
    /// A renewal's order id, scoped to the period it charges rather than to the subscription.
    /// </summary>
    /// <remarks>
    /// The payment module allows only one recurring payment per order id, ever — so an order id
    /// shared across every renewal would reject the second one outright. Scoping it to the
    /// period keeps it stable across retries within that period (which is what lets a crash
    /// between raising the charge and recording it be recovered by idempotency key) while
    /// guaranteeing the next period's renewal never collides with an id already used.
    /// </remarks>
    public static string RenewalOrderIdFor(string subscriptionId, string periodKey) =>
        $"{OrderIdPrefix}{subscriptionId}:{periodKey}";

    /// <summary>
    /// The idempotency key one renewal or dunning attempt is raised under.
    /// </summary>
    /// <remarks>
    /// Includes the attempt number because, unlike the initial charge, a dunning retry is a
    /// genuinely new charge attempt, not a replay of the last one — each must be free to
    /// succeed or fail independently of the attempt before it.
    /// </remarks>
    public static string RenewalKeyFor(string subscriptionId, string periodKey, int attempt) =>
        $"sub-renew:{subscriptionId}:{periodKey}:{attempt}";

    /// <summary>
    /// A plan change's order id, scoped to the version being changed from.
    /// </summary>
    /// <remarks>
    /// A plan change has no period key to scope by — it is not a renewal — but <c>Version</c> is
    /// already a monotonically increasing number unique to this exact attempt, which is all the
    /// "one recurring payment per order id, ever" rule needs to stay satisfied.
    /// </remarks>
    public static string PlanChangeOrderIdFor(string subscriptionId, int version) =>
        $"{OrderIdPrefix}{subscriptionId}:planchange:{version}";

    public static string PlanChangeKeyFor(string subscriptionId, int version) =>
        $"sub-planchange:{subscriptionId}:{version}";
}
