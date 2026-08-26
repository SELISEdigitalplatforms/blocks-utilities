using System.Security.Cryptography;
using System.Text;

using Subscription.DomainService.Enums;

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

    public const string SubscriptionQuantityChanged = "SubscriptionQuantityChanged";
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
    /// The idempotency key one card-collection attempt is raised under.
    /// </summary>
    /// <remarks>
    /// Carries the attempt number for the same reason a dunning retry does: a hosted session that
    /// expired cannot be reopened, so a second attempt has to be a new session, and the provider
    /// would replay the first one under the first key. Derived rather than random so a crash
    /// between opening the session and recording the link to it can still find it.
    /// </remarks>
    public static string PaymentMethodSetupKeyFor(string subscriptionId, int attempt) =>
        DeterministicKey($"sub-setup:{subscriptionId}:{attempt}");

    public static string PlanChangeKeyFor(string subscriptionId, int version) =>
        DeterministicKey($"sub-planchange:{subscriptionId}:{version}");

    /// <summary>
    /// The order a settlement is charged under, named for what it settles and scoped by the
    /// reservation it settles.
    /// </summary>
    /// <remarks>
    /// Scoped by the reservation rather than the version. A settlement writes its reservation before
    /// it spends anything, so that id is available and is the one identifier a concurrent change
    /// cannot move — which is exactly what a retry needs to find the charge it already raised instead
    /// of taking the money a second time.
    /// <para>
    /// The kind is in the id because invoice history reads it back: both kinds shared the
    /// <c>quantity:</c> form, so a plan-change invoice classified itself as a renewal, and the
    /// suffix it could not parse became the period key. The id is a label and a classifier, never the
    /// dedupe: that is <see cref="SettlementChargeKeyFor"/>, which is deliberately left alone here so
    /// a reservation taken before this change and replayed after it still finds its own attempt
    /// rather than raising a second.
    /// </para>
    /// <para>
    /// The segments are short because the whole id has to fit the payment module's 80-character
    /// order-id limit, and a subscription id and a reservation id already spend 68 of it. Spelling
    /// this "planchange" put it at 84 — which is how the existing "quantity" spelling turned out to
    /// have been two characters over the limit all along, untested.
    /// </para>
    /// <para>
    /// Rows charged before the kinds were told apart carry the old <c>quantity</c> spelling whichever
    /// kind they were, so a historical plan-change invoice reads as a quantity change. Better than
    /// reading as a renewal, and not worth rewriting settled financial records to improve. Both old
    /// spellings are still read — see <see cref="LegacyPlanChangeSegment"/> — and neither is written.
    /// </para>
    /// </remarks>
    public static string SettlementOrderIdFor(
        string subscriptionId,
        SettlementReservationKind kind,
        string reservationId) =>
        $"{OrderIdPrefix}{subscriptionId}:{SettlementSegmentFor(kind)}:{reservationId}";

    /// <summary>
    /// The segment naming a settlement's kind, shared by the writer and the reader so they cannot
    /// disagree about the spelling.
    /// </summary>
    public static string SettlementSegmentFor(SettlementReservationKind kind) =>
        kind switch
        {
            SettlementReservationKind.PlanChange => PlanChangeSegment,
            _ => QuantitySegment
        };

    public const string PlanChangeSegment = "pc";

    public const string QuantitySegment = "qty";

    public const string UsageSegment = "usage";

    /// <summary>
    /// Spellings written before the settlement kinds were distinguished and before the ids were
    /// shortened to fit the order-id limit. Read forever, written never: the rows carrying them are
    /// settled payments, and a financial record does not get rewritten to tidy up a string.
    /// </summary>
    public const string LegacyPlanChangeSegment = "planchange";

    /// <summary>
    /// The one both kinds shared. A row carrying it may be either, and is reported as a quantity
    /// change because that is what the great majority of them are.
    /// </summary>
    public const string LegacySettlementSegment = "quantity";

    public static string SettlementChargeKeyFor(string subscriptionId, string claimId) =>
        DeterministicKey($"sub-quantity:{subscriptionId}:{claimId}");

    /// <summary>
    /// The key a payment recorded from an already-settled invoice is written under.
    /// </summary>
    /// <remarks>
    /// Distinct from the key the charge attempt itself reserved, so the bookkeeping record can
    /// never collide with it. Stated here rather than built at each end: a sweep looking for a
    /// charge that a crash left unaccounted for has to look under the same name the gateway wrote,
    /// and two spellings of that name is how a paid-for increase gets released as unpaid.
    /// </remarks>
    public static string RecordedSettlementKeyFor(string chargeIdempotencyKey) =>
        $"{chargeIdempotencyKey}:settled";

    /// <summary>
    /// A usage invoice's order id, scoped to the period it charges and stable across every
    /// retry — unlike the idempotency key below, this must never change: a fresh order id per
    /// attempt would let the payment module's "one recurring payment per order id" rule be
    /// satisfied by a second, duplicate charge for the same period.
    /// </summary>
    public static string UsageInvoiceOrderIdFor(string subscriptionId, string periodKey) =>
        $"{OrderIdPrefix}{subscriptionId}:{UsageSegment}:{periodKey}";

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
