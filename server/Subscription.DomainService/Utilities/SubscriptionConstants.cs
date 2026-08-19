using System.Security.Cryptography;
using System.Text;

namespace Subscription.DomainService.Utilities;

public static class SubscriptionConstants
{
    /// <summary>
    /// Where subscription domain events are published. The worker consumes usage-threshold
    /// events from this topic and forwards a mail command to the platform mail module.
    /// </summary>
    public const string LifecycleTopic =
        "blocks_subscription_lifecycle_topic";

    public const string UsageThresholdEmailQueue =
        "blocks_subscription_usage_threshold_email_listener";
    public const string MailQueue = "blocks_email_listener";
    public const string UsageThresholdMailPurpose =
        "subscription_usage_threshold";
    public const string DefaultMailLanguage = "en-US";

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
    public const string UsageRated = "UsageRated";
    public const string UsageRatingFailed = "UsageRatingFailed";

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
        DeterministicKey($"sub-init:{subscriptionId}");

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
        DeterministicKey($"sub-renew:{subscriptionId}:{periodKey}:{attempt}");

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
        DeterministicKey($"sub-planchange:{subscriptionId}:{version}");

    /// <summary>
    /// A usage invoice's order id, scoped to the period it charges and stable across every
    /// retry — unlike the idempotency key below, this must never change: a fresh order id per
    /// attempt would let the payment module's "one recurring payment per order id" rule be
    /// satisfied by a second, duplicate charge for the same period.
    /// </summary>
    public static string UsageInvoiceOrderIdFor(string subscriptionId, string periodKey) =>
        $"{OrderIdPrefix}{subscriptionId}:usage:{periodKey}";

    /// <summary>
    /// The idempotency key one overage-charge attempt is raised under. Carries the attempt
    /// number for the same reason a renewal's dunning retry does — a retried charge must be
    /// free to succeed where the last one declined, not replay its cached failure.
    /// </summary>
    public static string UsageInvoiceKeyFor(string subscriptionId, string periodKey, int attempt) =>
        DeterministicKey($"sub-usage:{subscriptionId}:{periodKey}:{attempt}");

    /// <summary>
    /// A stable UUID for a logical idempotency key.
    /// </summary>
    /// <remarks>
    /// Two requirements meet here and only this satisfies both. The payment module refuses any
    /// idempotency key that does not parse as a UUID, and every charge this module raises has to
    /// be re-derivable from stored state — a random key would be lost with the process that
    /// raised it, taking with it the only way the recovery sweep can tell a charge that already
    /// happened from one that never did.
    /// <para>
    /// Hashing gives both: the same inputs produce the same UUID on every machine and every
    /// restart. The readable name survives as the hash input rather than in the key itself, so
    /// an initial charge and a renewal for one subscription can never collapse onto the same
    /// key. Half of a SHA-256 is far more than enough to keep them apart.
    /// </para>
    /// </remarks>
    private static string DeterministicKey(string logicalName) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(logicalName)).AsSpan(0, 16))
            .ToString();
}
