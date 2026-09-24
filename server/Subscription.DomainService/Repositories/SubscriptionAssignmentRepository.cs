using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The Mongo-backed seat register behind <see cref="ISubscriptionAssignmentRepository"/>.
/// </summary>
/// <remarks>
/// A new collection, read by nothing that exists today. Organization-wise subscriptions are reached
/// through the organization exactly as they always have been and never acquire a row here, which is
/// what lets seat assignment be added without touching a rule a live customer depends on.
/// </remarks>
public sealed class SubscriptionAssignmentRepository : ISubscriptionAssignmentRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionAssignmentRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Assignments(tenantId).Indexes.CreateManyAsync(
            SubscriptionAssignmentIndexDefinitions.CreateIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<SeatAssignmentOutcome> TryAssignAsync(
        SubscriptionAssignment assignment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        await EnsureIndexesAsync(assignment.TenantId, cancellationToken);

        // Insert first and let the unique index answer, rather than reading whether the seat is
        // free: two administrators assigning the same person at the same moment would both pass
        // that read, and one of them would be told it worked while only one row existed.
        assignment.ReleasedAtUtc = null;

        try
        {
            await Assignments(assignment.TenantId)
                .InsertOneAsync(assignment, cancellationToken: cancellationToken);

            return SeatAssignmentOutcome.Assigned;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return SeatAssignmentOutcome.AlreadyHeld;
        }
    }

    public async Task<SeatReleaseOutcome> TryReleaseAsync(
        string tenantId,
        string subscriptionId,
        string userId,
        DateTime releasedAtUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var result = await Assignments(tenantId).UpdateOneAsync(
            Builders<SubscriptionAssignment>.Filter.And(
                ActiveFilter(tenantId, subscriptionId),
                Builders<SubscriptionAssignment>.Filter.Eq(
                    assignment => assignment.UserId, userId)),
            Builders<SubscriptionAssignment>.Update
                .Set(assignment => assignment.ReleasedAtUtc, releasedAtUtc)
                .Set(assignment => assignment.LastUpdatedDateUtc, releasedAtUtc),
            cancellationToken: cancellationToken);

        // Nothing modified means there was no live seat. The filter already excludes released
        // rows, so this cannot be a seat given up twice being reported as a release.
        return result.ModifiedCount == 1
            ? SeatReleaseOutcome.Released
            : SeatReleaseOutcome.NotHeld;
    }

    public async Task<IReadOnlyList<string>> ListSubscriptionIdsForUserAsync(
        string tenantId,
        string organizationId,
        string userId,
        CancellationToken cancellationToken)
    {
        var held = await Assignments(tenantId)
            .Find(Builders<SubscriptionAssignment>.Filter.And(
                Builders<SubscriptionAssignment>.Filter.Eq(
                    assignment => assignment.TenantId, tenantId),
                Builders<SubscriptionAssignment>.Filter.Eq(
                    assignment => assignment.OrganizationId, organizationId),
                Builders<SubscriptionAssignment>.Filter.Eq(
                    assignment => assignment.UserId, userId),
                Builders<SubscriptionAssignment>.Filter.Eq(
                    assignment => assignment.ReleasedAtUtc, null)))
            .Project(assignment => assignment.SubscriptionId)
            .ToListAsync(cancellationToken);

        return [.. held];
    }

    public async Task<IReadOnlyList<SubscriptionAssignment>> ListActiveAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken) =>
        await Assignments(tenantId)
            .Find(ActiveFilter(tenantId, subscriptionId))
            .ToListAsync(cancellationToken);

    public async Task<long> CountActiveAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken) =>
        await Assignments(tenantId)
            .CountDocumentsAsync(
                ActiveFilter(tenantId, subscriptionId),
                cancellationToken: cancellationToken);

    public async Task<long> ReleaseAllAsync(
        string tenantId,
        string subscriptionId,
        DateTime releasedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Assignments(tenantId).UpdateManyAsync(
            ActiveFilter(tenantId, subscriptionId),
            Builders<SubscriptionAssignment>.Update
                .Set(assignment => assignment.ReleasedAtUtc, releasedAtUtc)
                .Set(assignment => assignment.LastUpdatedDateUtc, releasedAtUtc),
            cancellationToken: cancellationToken);

        return result.ModifiedCount;
    }

    /// <summary>
    /// One subscription's seats that are still held.
    /// </summary>
    /// <remarks>
    /// Tenant is in the filter as well as selecting the database. Redundant in production, where a
    /// tenant is its own database, and not redundant anywhere a test harness maps several onto one
    /// — which is where a missing tenant predicate silently reads another tenant's rows.
    /// </remarks>
    private static FilterDefinition<SubscriptionAssignment> ActiveFilter(
        string tenantId,
        string subscriptionId) =>
        Builders<SubscriptionAssignment>.Filter.And(
            Builders<SubscriptionAssignment>.Filter.Eq(
                assignment => assignment.TenantId, tenantId),
            Builders<SubscriptionAssignment>.Filter.Eq(
                assignment => assignment.SubscriptionId, subscriptionId),
            Builders<SubscriptionAssignment>.Filter.Eq(
                assignment => assignment.ReleasedAtUtc, null));

    private IMongoCollection<SubscriptionAssignment> Assignments(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionAssignment>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.Assignments);
}
