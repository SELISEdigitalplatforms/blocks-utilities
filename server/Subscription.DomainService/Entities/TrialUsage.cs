using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One subscriber's claim on one plan's trial.
/// </summary>
/// <remarks>
/// This is what makes "one trial per plan" true rather than merely likely. Without it a subscriber
/// could cancel and sign up again, and get the trial again, for as long as they cared to.
/// <para>
/// Who "the subscriber" is follows the plan's <see cref="SubscriberScope"/>: the organization for
/// an organization-wise plan, and the person who bought it for a user-wise one, since a user-wise
/// subscription is still held by an organization and several of them can sit side by side. The
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

    /// <summary>The organization id for an organization-wise plan, the buyer's user id for a user-wise one.</summary>
    public string SubjectId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    /// <summary>The organization that held the subscription, kept for audit whatever the scope.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public TrialUsageState State { get; set; } = TrialUsageState.Claimed;

    public DateTime ClaimedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }

    /// <summary>
    /// Who claims a trial on <paramref name="planCode"/> for this subscription, or null when nobody
    /// can be identified: a user-wise signup made without a user — an API key, or the console acting
    /// for an organization. Those are deliberately not limited, by product decision, rather than
    /// all sharing one empty-string slot.
    /// </summary>
    public static TrialUsage? For(
        SubscriptionDetail subscription,
        SubscriberScope scope,
        string planCode,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var subjectId = SubjectOf(scope, subscription.OrganizationId, subscription.PurchasedBy?.UserId);

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

    /// <summary>Who a trial on a plan of this scope belongs to, or null when nobody can be identified.</summary>
    public static string? SubjectOf(SubscriberScope scope, string organizationId, string? buyerUserId)
    {
        var subjectId = scope == SubscriberScope.User ? buyerUserId : organizationId;
        return string.IsNullOrEmpty(subjectId) ? null : subjectId;
    }
}
