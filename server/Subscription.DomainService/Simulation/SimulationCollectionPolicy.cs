using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Simulation;

/// <summary>
/// What the data console may do to one collection, and nothing it does not explicitly say.
/// </summary>
/// <remarks>
/// A code-level allowlist rather than configuration: an operator can turn the console on or off,
/// but cannot widen what it reaches by editing a config file. Every field named here must exist
/// on the real document — there is no schema validation beyond "is it in this list."
/// </remarks>
public sealed record SimulationCollectionPolicy(
    string LogicalName,
    string CollectionName,
    /// <summary>The document field a subscription id is matched against in this collection.</summary>
    string SubscriptionIdField,
    bool CanRead,
    /// <summary>
    /// Always false in this version — see the class remark on
    /// <see cref="SubscriptionSimulationDataConsolePolicy"/> for why <c>insertOne</c> is not
    /// implemented at all yet, kept as a field for when a genuinely safe target exists.
    /// </summary>
    bool CanInsert,
    /// <summary>
    /// Field names writable by <c>updateOne</c>, restricted to UTC timestamps in this version —
    /// see <see cref="UpdateDataFieldRequest"/>. Even these are better changed through
    /// the purpose-built actions in PRs 2-5; this exists for what those cannot yet reach.
    /// </summary>
    IReadOnlyList<string> UpdatableFields);

public static class SubscriptionSimulationDataConsolePolicy
{
    /// <summary>
    /// No collection allows <c>insertOne</c> yet — every candidate row this harness could safely
    /// insert (a subscription, a payment link, a usage invoice) is already reachable through a
    /// purpose-built action from PRs 2-5, and inventing one here would be exactly the free-form
    /// database write this endpoint exists to avoid becoming.
    /// </summary>
    public static readonly IReadOnlyList<SimulationCollectionPolicy> Collections =
    [
        new(
            LogicalName: "subscriptions",
            CollectionName: SubscriptionCollections.Subscriptions,
            SubscriptionIdField: "_id",
            CanRead: true, CanInsert: false,
            UpdatableFields: ["NextFeeBillingAtUtc", "CurrentUsagePeriodEndUtc", "NextUsageBillingAtUtc"]),
        new(
            LogicalName: "usage-invoices",
            CollectionName: SubscriptionCollections.UsageInvoices,
            SubscriptionIdField: "SubscriptionId",
            CanRead: true, CanInsert: false,
            UpdatableFields: ["NextAttemptAtUtc"]),
        new(
            LogicalName: "payment-links",
            CollectionName: SubscriptionCollections.PaymentLinks,
            SubscriptionIdField: "SubscriptionId",
            CanRead: true, CanInsert: false,
            UpdatableFields: ["NextCheckAtUtc"]),
        new(
            LogicalName: "audit-events",
            CollectionName: SubscriptionCollections.AuditEvents,
            SubscriptionIdField: "SubscriptionId",
            CanRead: true, CanInsert: false,
            UpdatableFields: []),
        new(
            LogicalName: "simulation-runs",
            CollectionName: SubscriptionCollections.SimulationRuns,
            SubscriptionIdField: "SubscriptionId",
            CanRead: true, CanInsert: false,
            UpdatableFields: []),
    ];

    public static SimulationCollectionPolicy? Find(string logicalName) =>
        Collections.FirstOrDefault(
            policy => string.Equals(policy.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));
}
