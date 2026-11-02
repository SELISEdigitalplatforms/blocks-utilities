using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One organization's claim on one plan's trial.
/// </summary>
/// <remarks>
/// This is what makes "one trial per plan" true rather than merely likely. Without it an
/// organization could cancel and sign up again, and get the trial again, for as long as it cared
/// to. Only written for a plan with <see cref="Plan.OneTrialPerPlan"/> on.
/// <para>
/// The claim belongs to the organization whatever the plan's <see cref="SubscriberScope"/>. Keyed on
/// the buyer, a user-wise plan let an organization trial it again by switching who bought it, and
/// refused a person's trial for an organization that had never had it because they had used it
/// elsewhere. The scope stays on the record and in the index so rows written before this — keyed on
/// the buyer — stay valid alongside; see <see cref="Repositories.TrialUsageRepository"/>. The
/// plan is identified by its <see cref="PlanSnapshot.Code"/>, which survives catalogue edits — an
/// id would give every edited plan a fresh trial.
/// </para>
/// <para>
/// Claimed at signup rather than at trial start, so two signups racing for the same trial are
/// settled by <see cref="Repositories.TrialUsageIndexDefinitions"/>'s unique index before either
/// takes a card. A claim only becomes permanent once the subscription actually reaches its
/// trial; an abandoned checkout gives it back. Never deleted.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class TrialUsage
{
    [BsonId] public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public SubscriberScope Scope { get; set; }

    /// <summary>
    /// The organization id. A user-wise row written before trials were per organization holds the
    /// buyer's user id instead, and is never read again.
    /// </summary>
    public string SubjectId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    /// <summary>The organization that held the subscription. Equal to <see cref="SubjectId"/> on every row written now.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public TrialUsageState State { get; set; } = TrialUsageState.Claimed;

    public DateTime ClaimedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }

    /// <summary>
    /// The claim this subscription makes on <paramref name="planCode"/>'s trial, or null when it has
    /// no organization to make it for — rather than all such signups sharing one empty-string slot.
    /// A user-wise signup made without a user, by an API key or the console, claims for its
    /// organization like any other.
    /// </summary>
    public static TrialUsage? For(
        SubscriptionDetail subscription,
        SubscriberScope scope,
        string planCode,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var subjectId = SubjectOf(subscription.OrganizationId);

        return subjectId is null
            ? null
            : new TrialUsage
            {
                TenantId = subscription.TenantId,
                Scope = scope,
                SubjectId = subjectId,
                PlanCode = planCode,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                ClaimedAtUtc = nowUtc
            };
    }

    /// <summary>Who a trial belongs to — the organization — or null when there is none.</summary>
    public static string? SubjectOf(string organizationId) =>
        string.IsNullOrEmpty(organizationId) ? null : organizationId;
}
