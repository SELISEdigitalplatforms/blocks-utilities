using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The index that makes "one trial per plan" true, rather than merely checked.
/// </summary>
public static class TrialUsageIndexDefinitions
{
    public const string OneTrialIndexName = "trial_usage_one_per_subject_and_plan";
    public const string SubscriptionLookupIndexName = "trial_usage_by_subscription";

    public static IReadOnlyCollection<CreateIndexModel<TrialUsage>> CreateIndexes() =>
    [
        // One claim that is not Released per subscriber and plan. Released rows stay outside the
        // filter so an abandoned signup keeps its own history and a later signup still has room to
        // insert. "$lt Released" because a partial filter cannot express "$ne" — the same constraint
        // CampaignRedemptionIndexDefinitions documents, and the reason Released is the last state.
        new(
            Builders<TrialUsage>.IndexKeys
                .Ascending(usage => usage.TenantId)
                .Ascending(usage => usage.Scope)
                .Ascending(usage => usage.SubjectId)
                .Ascending(usage => usage.PlanCode),
            new CreateIndexOptions<TrialUsage>
            {
                Unique = true,
                Name = OneTrialIndexName,
                PartialFilterExpression = new BsonDocument(
                    nameof(TrialUsage.State),
                    new BsonDocument("$lt", (int)TrialUsageState.Released))
            }),
        new(
            Builders<TrialUsage>.IndexKeys
                .Ascending(usage => usage.TenantId)
                .Ascending(usage => usage.SubscriptionId)
                .Ascending(usage => usage.PlanCode),
            new CreateIndexOptions<TrialUsage> { Name = SubscriptionLookupIndexName })
    ];
}
