using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The Mongo-backed store behind <see cref="IMailDeliveryReportRepository"/>.
/// </summary>
/// <remarks>
/// Insert-only. There is no update path and no upsert: a report describes one handover that either
/// happened or did not, and editing it after the fact would make the history less trustworthy than
/// the <c>Delivery</c> block it exists to complement, which is overwritten on every resend.
/// </remarks>
public sealed class MailDeliveryReportRepository : IMailDeliveryReportRepository
{
    private const int MaxLimit = 500;

    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public MailDeliveryReportRepository(IDbContextProvider db) => _db = db;

    public async Task AddAsync(MailDeliveryReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        await EnsureIndexesAsync(report.TenantId, cancellationToken);

        await Collection(report.TenantId)
            .InsertOneAsync(report, options: null, cancellationToken);
    }

    public async Task<IReadOnlyList<MailDeliveryReport>> ForSubjectAsync(
        string tenantId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        return await Collection(tenantId)
            .Find(report => report.TenantId == tenantId && report.SubjectId == subjectId)
            .SortByDescending(report => report.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MailDeliveryReport>> RecentAsync(
        string tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        return await Collection(tenantId)
            .Find(report => report.TenantId == tenantId)
            .SortByDescending(report => report.CreatedAtUtc)
            .Limit(Math.Clamp(limit, 1, MaxLimit))
            .ToListAsync(cancellationToken);
    }

    private IMongoCollection<MailDeliveryReport> Collection(string tenantId) =>
        SubscriptionCollections.Of<MailDeliveryReport>(
            _db, tenantId, SubscriptionCollections.MailDeliveryReports);

    // Created once per tenant per process, the same way every other repository here does it. The
    // dictionary is the guard: index creation is idempotent in MongoDB but not free, and this runs
    // on a path that fires once per mail.
    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (!_indexed.TryAdd(tenantId, 0))
        {
            return;
        }

        try
        {
            await Collection(tenantId).Indexes.CreateManyAsync(
                MailDeliveryReportIndexDefinitions.CreateIndexes(), cancellationToken);
        }
        catch
        {
            // Left to be retried by the next call rather than swallowed for the life of the
            // process: a transient failure here would otherwise leave this tenant permanently
            // unindexed, and the collection is read by operators during an incident.
            _indexed.TryRemove(tenantId, out _);
            throw;
        }
    }
}
