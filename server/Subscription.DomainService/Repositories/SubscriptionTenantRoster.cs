using System.Collections.Concurrent;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The tenants subscription background work has any reason to visit: every tenant that has ever
/// created a subscription.
/// </summary>
/// <remarks>
/// This used to be the platform's whole tenant registry. On prod that is 3,104 tenants, of which 8
/// had ever queued subscription work (2026-10-08) -- so each repair pass made a query per tenant
/// to find nothing for nearly all of them, and about a thousand had no database to query at all,
/// costing an error and a warning apiece every pass.
/// <para>
/// Not derived from the work queue. A live subscription need not have a pending row there -- one
/// whose renewal was never announced is exactly what the repair sweep exists to find, and its
/// completed rows are purged -- so a queue-derived roster would drop the very tenants the sweep is
/// for. Creation records the tenant here instead, before the subscription exists
/// (<see cref="ISubscriptionTenantRoster"/>), and nothing is ever removed.
/// </para>
/// <para>
/// Tenants that subscribed before this record existed are added once per environment by
/// <see cref="BackfillAsync"/>, the one remaining walk of the whole registry, guarded by a marker
/// so it never runs twice.
/// </para>
/// <para>
/// Addressed by connection string and database name directly, like the queue: background work has
/// no ambient tenant to resolve from.
/// </para>
/// </remarks>
public sealed class SubscriptionTenantRoster : ISubscriptionTenantSource, ISubscriptionTenantRoster
{
    private const string RosterCollection = "SubscriptionTenants";
    private const string TenantRegistryCollection = "Tenants";
    private const string SubscriptionCollection = SubscriptionCollections.Subscriptions;

    /// <summary>Written once the backfill has finished; kept in the roster collection, never listed.</summary>
    private const string BackfillMarkerId = "$backfill-complete";

    /// <summary>Used only when the secret carries no root database name.</summary>
    private const string FallbackRootDatabase = "BlocksRootDb";

    private const string TenantIdField = "TenantId";

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IBlocksSecret _secret;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionTenantRoster> _logger;

    /// <summary>
    /// Tenants this process has already recorded, so a busy tenant costs one root write per
    /// process rather than one per subscription. Only ever added to after the write succeeds.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _recorded = new(StringComparer.Ordinal);

    private bool _backfilled;

    public SubscriptionTenantRoster(
        IDbContextProvider dbContextProvider,
        IBlocksSecret secret,
        TimeProvider? time = null,
        ILogger<SubscriptionTenantRoster>? logger = null)
    {
        _dbContextProvider = dbContextProvider;
        _secret = secret;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SubscriptionTenantRoster>.Instance;
    }

    public async Task RecordAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (_recorded.ContainsKey(tenantId))
        {
            return;
        }

        await UpsertAsync(tenantId, cancellationToken);
        _recorded.TryAdd(tenantId, 0);
    }

    public async Task<IReadOnlyList<string>> ListTenantIdsAsync(
        CancellationToken cancellationToken)
    {
        if (!_backfilled)
        {
            await BackfillAsync(cancellationToken);
            _backfilled = true;
        }

        return await Roster()
            .Find(Builders<SubscriptionTenant>.Filter.Ne(tenant => tenant.TenantId, BackfillMarkerId))
            .Project(tenant => tenant.TenantId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Records every registry tenant that already has a subscription, once per environment.
    /// </summary>
    /// <remarks>
    /// A tenant with no database cannot have a subscription, so it is skipped. Any other failure
    /// throws before the marker is written, and the next roster read starts again: skipping a
    /// tenant that was only briefly unreachable would leave it off the roster for good.
    /// </remarks>
    private async Task BackfillAsync(CancellationToken cancellationToken)
    {
        if (await Roster().Find(tenant => tenant.TenantId == BackfillMarkerId).AnyAsync(cancellationToken))
        {
            return;
        }

        var registry = await Root()
            .GetCollection<BsonDocument>(TenantRegistryCollection)
            .Find(Builders<BsonDocument>.Filter.Exists(TenantIdField))
            .Project(Builders<BsonDocument>.Projection.Include(TenantIdField))
            .ToListAsync(cancellationToken);

        var tenantIds = registry
            .Select(document => document.GetValue(TenantIdField, BsonNull.Value))
            .Where(value => value.IsString && !string.IsNullOrWhiteSpace(value.AsString))
            .Select(value => value.AsString)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var recorded = 0;
        var withoutDatabase = 0;

        foreach (var tenantId in tenantIds)
        {
            bool hasSubscription;

            try
            {
                hasSubscription = await _dbContextProvider
                    .GetDatabase(tenantId)
                    .GetCollection<BsonDocument>(SubscriptionCollection)
                    .Find(FilterDefinition<BsonDocument>.Empty)
                    .Limit(1)
                    .AnyAsync(cancellationToken);
            }
            catch (Exception exception) when (exception.GetBaseException() is KeyNotFoundException)
            {
                withoutDatabase++;
                continue;
            }

            if (hasSubscription)
            {
                await UpsertAsync(tenantId, cancellationToken);
                recorded++;
            }
        }

        await UpsertAsync(BackfillMarkerId, cancellationToken);

        _logger.LogInformation(
            "Subscription tenant roster backfilled RegistryTenants={RegistryTenants} " +
            "Recorded={Recorded} WithoutDatabase={WithoutDatabase}",
            tenantIds.Count,
            recorded,
            withoutDatabase);
    }

    private Task UpsertAsync(string tenantId, CancellationToken cancellationToken) =>
        Roster().UpdateOneAsync(
            Builders<SubscriptionTenant>.Filter.Eq(tenant => tenant.TenantId, tenantId),
            Builders<SubscriptionTenant>.Update
                .SetOnInsert(tenant => tenant.FirstSeenAtUtc, _time.GetUtcNow().UtcDateTime),
            new UpdateOptions { IsUpsert = true },
            cancellationToken);

    private IMongoCollection<SubscriptionTenant> Roster() =>
        Root().GetCollection<SubscriptionTenant>(RosterCollection);

    private IMongoDatabase Root()
    {
        var rootDatabase = string.IsNullOrWhiteSpace(_secret.RootDatabaseName)
            ? FallbackRootDatabase
            : _secret.RootDatabaseName;

        return _dbContextProvider.GetDatabase(_secret.DatabaseConnectionString, rootDatabase);
    }

    /// <summary>One tenant on the roster, keyed by its id so recording it twice is one document.</summary>
    private sealed class SubscriptionTenant
    {
        [BsonId]
        public string TenantId { get; set; } = string.Empty;

        public DateTime FirstSeenAtUtc { get; set; }
    }
}
