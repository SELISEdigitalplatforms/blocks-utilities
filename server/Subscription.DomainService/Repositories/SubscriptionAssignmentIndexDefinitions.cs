using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The indexes behind seat assignment.
/// </summary>
/// <remarks>
/// Created by <see cref="SubscriptionAssignmentRepository.EnsureIndexesAsync"/> on a tenant's first
/// touch, the same way every other collection here gets its own.
/// </remarks>
public static class SubscriptionAssignmentIndexDefinitions
{
    public const string ActiveMembershipIndexName = "ux_assignment_subscription_member_active";
    public const string ActiveSeatIndexName = "ux_assignment_subscription_seat_active";
    public const string SubscriberLookupIndexName = "ix_assignment_tenant_org_user_active";
    public const string SubscriptionLookupIndexName = "ix_assignment_tenant_subscription_active";

    public static IReadOnlyCollection<CreateIndexModel<SubscriptionAssignment>> CreateIndexes() =>
    [
        // One live seat per person per subscription, enforced by the database rather than by a
        // read-then-write: two administrators assigning the same person at once would both pass
        // the read, and the subscription would then look one seat fuller than it is.
        //
        // Deliberately not unique across subscriptions. A person may hold a seat on more than one
        // -- an allowance plan and a storage plan are different purchases -- so the only thing
        // that cannot happen twice is the same seat on the same subscription.
        //
        // Released rows are outside the filter, not merely ignored by readers. A seat handed back
        // and later handed to the same person again is two separate records with two separate
        // histories, and had the released one still counted here the second assignment would have
        // had nowhere to insert.
        //
        // The filter matches BSON null, which means an active row must actually carry
        // ReleasedAtUtc: null -- a document that omitted the field would fall outside the index and
        // be exempt from the rule it exists to enforce. Nothing marks the property
        // BsonIgnoreIfNull, so the driver writes the null; that is load-bearing and not a detail to
        // tidy away. ($exists: false would say this directly, but a partial filter may only use
        // $exists: true.)
        new(
            Builders<SubscriptionAssignment>.IndexKeys
                .Ascending(assignment => assignment.TenantId)
                .Ascending(assignment => assignment.SubscriptionId)
                .Ascending(assignment => assignment.UserId),
            new CreateIndexOptions<SubscriptionAssignment>
            {
                Unique = true,
                Name = ActiveMembershipIndexName,
                PartialFilterExpression = new BsonDocument(
                    nameof(SubscriptionAssignment.ReleasedAtUtc),
                    new BsonDocument("$type", "null"))
            }),

        // One person per seat, enforced by the database for the same reason the rule above is:
        // two administrators assigning into the last free seat would both read it as free. This is
        // the other half of the pair — that one stops a person holding two seats on a
        // subscription, this one stops a seat holding two people.
        new(
            Builders<SubscriptionAssignment>.IndexKeys
                .Ascending(assignment => assignment.TenantId)
                .Ascending(assignment => assignment.SubscriptionId)
                .Ascending(assignment => assignment.SeatNumber),
            new CreateIndexOptions<SubscriptionAssignment>
            {
                Unique = true,
                Name = ActiveSeatIndexName,
                PartialFilterExpression = new BsonDocument(
                    nameof(SubscriptionAssignment.ReleasedAtUtc),
                    new BsonDocument("$type", "null"))
            }),

        // What entitlement asks on every gated action: which subscriptions does this person hold a
        // seat on. Organization first so the query is answered without scanning another
        // organization's rows.
        new(
            Builders<SubscriptionAssignment>.IndexKeys
                .Ascending(assignment => assignment.TenantId)
                .Ascending(assignment => assignment.OrganizationId)
                .Ascending(assignment => assignment.UserId)
                .Ascending(assignment => assignment.ReleasedAtUtc),
            new CreateIndexOptions { Name = SubscriberLookupIndexName }),

        // What an administrator asks: who is sitting on this subscription's seats, and how many
        // are left. Also what release has to find when a subscription ends.
        new(
            Builders<SubscriptionAssignment>.IndexKeys
                .Ascending(assignment => assignment.TenantId)
                .Ascending(assignment => assignment.SubscriptionId)
                .Ascending(assignment => assignment.ReleasedAtUtc),
            new CreateIndexOptions { Name = SubscriptionLookupIndexName })
    ];
}
