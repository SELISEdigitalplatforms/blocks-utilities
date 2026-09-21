using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Reporting;

/// <summary>
/// Reads the subscription collections for reporting. Writes nothing.
/// </summary>
/// <remarks>
/// Aggregation runs on the database rather than in this process. The alternative — pulling a
/// tenant's ledger back and summing it here — moves a month of records over the wire to produce a
/// dozen numbers, and gets slower in proportion to how successful the tenant is.
/// <para>
/// Indexes are ensured lazily per tenant, the same way every other repository in this module does
/// it: there is no startup index pass, because a tenant database can appear at any time. The two
/// index shapes reporting needs are declared alongside the collections they sit on rather than in
/// a set of their own, so the repository that owns each collection creates them too and the
/// definitions cannot drift apart.
/// </para>
/// </remarks>
public sealed class SubscriptionReportingRepository : ISubscriptionReportingRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public SubscriptionReportingRepository(IDbContextProvider db) => _db = db;

    public async Task<IReadOnlyList<UsageLedgerBucket>> AggregateUsageAsync(
        string tenantId,
        string? meterKey,
        DateTime fromUtc,
        DateTime toUtc,
        bool byMonth,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var filters = new List<FilterDefinition<SubscriptionUsageRecord>>
        {
            Builders<SubscriptionUsageRecord>.Filter.Eq(record => record.TenantId, tenantId),
            Builders<SubscriptionUsageRecord>.Filter.Gte(record => record.OccurredAtUtc, fromUtc),
            Builders<SubscriptionUsageRecord>.Filter.Lt(record => record.OccurredAtUtc, toUtc)
        };

        if (!string.IsNullOrWhiteSpace(meterKey))
        {
            filters.Add(
                Builders<SubscriptionUsageRecord>.Filter.Eq(record => record.MeterKey, meterKey));
        }

        // Always grouped by day, then folded into months here when months were asked for. One
        // pipeline instead of two: a capped range is at most 366 days, so the widest possible
        // result is a few hundred rows per meter, and a second grouping shape would be a second
        // thing to get wrong for no measurable gain.
        var grouped = await Records(tenantId)
            .Aggregate()
            .Match(Builders<SubscriptionUsageRecord>.Filter.And(filters))
            .Group(
                record => new
                {
                    record.MeterKey,
                    record.OccurredAtUtc.Year,
                    record.OccurredAtUtc.Month,
                    record.OccurredAtUtc.Day,
                    record.EntryType
                },
                group => new
                {
                    group.Key,
                    Quantity = group.Sum(record => record.Delta),
                    RecordCount = group.LongCount()
                })
            .ToListAsync(cancellationToken);

        return [.. grouped
            .GroupBy(entry => new
            {
                entry.Key.MeterKey,
                BucketStartUtc = new DateTime(
                    entry.Key.Year,
                    entry.Key.Month,
                    byMonth ? 1 : entry.Key.Day,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc),
                entry.Key.EntryType
            })
            .Select(bucket => new UsageLedgerBucket(
                bucket.Key.MeterKey,
                bucket.Key.BucketStartUtc,
                bucket.Key.EntryType,
                bucket.Sum(entry => entry.Quantity),
                bucket.Sum(entry => entry.RecordCount)))
            .OrderBy(bucket => bucket.BucketStartUtc)
            .ThenBy(bucket => bucket.MeterKey, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<SubscriptionDetail>> ListByStatusAsync(
        string tenantId,
        IReadOnlyCollection<SubscriptionStatus> statuses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        await EnsureIndexesAsync(tenantId, cancellationToken);

        if (statuses.Count == 0)
        {
            return [];
        }

        var filter = Builders<SubscriptionDetail>.Filter.And(
            Builders<SubscriptionDetail>.Filter.Eq(
                subscription => subscription.TenantId, tenantId),
            Builders<SubscriptionDetail>.Filter.In(
                subscription => subscription.Status, statuses));

        return await Subscriptions(tenantId)
            .Find(filter)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentRevenueBucket>> AggregateDocumentRevenueAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var filter = Builders<SubscriptionFinancialDocument>.Filter.And(
            Builders<SubscriptionFinancialDocument>.Filter.Eq(
                document => document.TenantId, tenantId),
            Builders<SubscriptionFinancialDocument>.Filter.Gte(
                document => document.IssuedAtUtc, fromUtc),
            Builders<SubscriptionFinancialDocument>.Filter.Lt(
                document => document.IssuedAtUtc, toUtc));

        var grouped = await Documents(tenantId)
            .Aggregate()
            .Match(filter)
            .Group(
                document => new { document.CurrencyCode, document.DocumentType },
                group => new
                {
                    group.Key,
                    TotalMinor = group.Sum(document => document.Amounts.TotalMinor),
                    DocumentCount = group.LongCount()
                })
            .ToListAsync(cancellationToken);

        return [.. grouped.Select(entry => new DocumentRevenueBucket(
            entry.Key.CurrencyCode,
            entry.Key.DocumentType,
            entry.TotalMinor,
            entry.DocumentCount))];
    }

    public async Task<IReadOnlyList<OverageRevenueBucket>> AggregateOverageRevenueAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        // Dated by when the invoice was last written rather than when it was created: a usage
        // invoice is created pending and becomes revenue only when it settles, which can be a
        // retry cycle later and belongs in the window it settled in.
        var filter = Builders<SubscriptionUsageInvoice>.Filter.And(
            Builders<SubscriptionUsageInvoice>.Filter.Eq(invoice => invoice.TenantId, tenantId),
            Builders<SubscriptionUsageInvoice>.Filter.Eq(
                invoice => invoice.State, SubscriptionUsageInvoiceState.Charged),
            Builders<SubscriptionUsageInvoice>.Filter.Gte(
                invoice => invoice.LastUpdatedDateUtc, fromUtc),
            Builders<SubscriptionUsageInvoice>.Filter.Lt(
                invoice => invoice.LastUpdatedDateUtc, toUtc));

        var grouped = await UsageInvoices(tenantId)
            .Aggregate()
            .Match(filter)
            .Group(
                invoice => invoice.CurrencyCode,
                group => new
                {
                    CurrencyCode = group.Key,
                    TotalMinor = group.Sum(invoice => invoice.TotalAmountMinor),
                    InvoiceCount = group.LongCount()
                })
            .ToListAsync(cancellationToken);

        return [.. grouped.Select(entry => new OverageRevenueBucket(
            entry.CurrencyCode,
            entry.TotalMinor,
            entry.InvoiceCount))];
    }

    public async Task<IReadOnlyList<SubscriptionUsageInvoice>> ListRetryingUsageInvoicesAsync(
        string tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var filter = Builders<SubscriptionUsageInvoice>.Filter.And(
            Builders<SubscriptionUsageInvoice>.Filter.Eq(invoice => invoice.TenantId, tenantId),
            Builders<SubscriptionUsageInvoice>.Filter.Eq(
                invoice => invoice.State, SubscriptionUsageInvoiceState.Pending),
            Builders<SubscriptionUsageInvoice>.Filter.Gt(invoice => invoice.AttemptCount, 0));

        return await UsageInvoices(tenantId)
            .Find(filter)
            .SortBy(invoice => invoice.NextAttemptAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<SubscriptionRosterPage> ListSubscriptionsAsync(
        string tenantId,
        int pageSize,
        SubscriptionReportCursor? after,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var filters = new List<FilterDefinition<SubscriptionDetail>>
        {
            Builders<SubscriptionDetail>.Filter.Eq(subscription => subscription.TenantId, tenantId)
        };

        if (after is not null)
        {
            // Keyset, not offset: a subscription created while somebody is paging would otherwise
            // shift every later page by one and hide a row that was never seen.
            filters.Add(
                Builders<SubscriptionDetail>.Filter.Or(
                    Builders<SubscriptionDetail>.Filter.Lt(
                        subscription => subscription.CreatedAtUtc, after.CreatedAtUtc),
                    Builders<SubscriptionDetail>.Filter.And(
                        Builders<SubscriptionDetail>.Filter.Eq(
                            subscription => subscription.CreatedAtUtc, after.CreatedAtUtc),
                        Builders<SubscriptionDetail>.Filter.Lt(
                            subscription => subscription.ItemId, after.SubscriptionId))));
        }

        var items = await Subscriptions(tenantId)
            .Find(Builders<SubscriptionDetail>.Filter.And(filters))
            .Sort(Builders<SubscriptionDetail>.Sort
                .Descending(subscription => subscription.CreatedAtUtc)
                .Descending(subscription => subscription.ItemId))
            .Limit(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > pageSize;

        return new SubscriptionRosterPage(
            hasMore ? items.GetRange(0, pageSize) : items,
            hasMore);
    }

    public async Task<IReadOnlyList<SubscriptionUsageCurrent>> ListCurrentUsageAsync(
        string tenantId,
        IReadOnlyCollection<string> subscriptionIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriptionIds);

        if (subscriptionIds.Count == 0)
        {
            return [];
        }

        var filter = Builders<SubscriptionUsageCurrent>.Filter.And(
            Builders<SubscriptionUsageCurrent>.Filter.Eq(current => current.TenantId, tenantId),
            Builders<SubscriptionUsageCurrent>.Filter.In(
                current => current.SubscriptionId, subscriptionIds));

        return await UsageCurrent(tenantId)
            .Find(filter)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CouponUptakeBucket>> AggregateCouponUptakeAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        // A missing Discount compares equal to null in Mongo, so this one predicate excludes both
        // subscriptions that never carried a code and any that carry an empty one.
        var filter = Builders<SubscriptionDetail>.Filter.And(
            Builders<SubscriptionDetail>.Filter.Eq(subscription => subscription.TenantId, tenantId),
            Builders<SubscriptionDetail>.Filter.Gte(
                subscription => subscription.CreatedAtUtc, fromUtc),
            Builders<SubscriptionDetail>.Filter.Lt(subscription => subscription.CreatedAtUtc, toUtc),
            Builders<SubscriptionDetail>.Filter.Ne(subscription => subscription.Discount!.Code, null!),
            Builders<SubscriptionDetail>.Filter.Ne(
                subscription => subscription.Discount!.Code, string.Empty));

        var grouped = await Subscriptions(tenantId)
            .Aggregate()
            .Match(filter)
            .Group(
                subscription => new { subscription.Discount!.Code, subscription.OrganizationId },
                group => new { group.Key, SubscriptionCount = group.LongCount() })
            .ToListAsync(cancellationToken);

        return [.. grouped.Select(entry => new CouponUptakeBucket(
            entry.Key.Code,
            entry.Key.OrganizationId,
            entry.SubscriptionCount))];
    }

    public async Task<IReadOnlyList<CouponRevenueBucket>> AggregateCouponRevenueAsync(
        string tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var filter = Builders<SubscriptionFinancialDocument>.Filter.And(
            Builders<SubscriptionFinancialDocument>.Filter.Eq(
                document => document.TenantId, tenantId),
            Builders<SubscriptionFinancialDocument>.Filter.Gte(
                document => document.IssuedAtUtc, fromUtc),
            Builders<SubscriptionFinancialDocument>.Filter.Lt(
                document => document.IssuedAtUtc, toUtc),
            Builders<SubscriptionFinancialDocument>.Filter.Ne(
                document => document.Amounts.PromotionCode, null),
            Builders<SubscriptionFinancialDocument>.Filter.Ne(
                document => document.Amounts.PromotionCode, string.Empty));

        var grouped = await Documents(tenantId)
            .Aggregate()
            .Match(filter)
            .Group(
                document => new { document.Amounts.PromotionCode, document.CurrencyCode },
                group => new
                {
                    group.Key,
                    RevenueMinor = group.Sum(document => document.Amounts.TotalMinor),
                    DiscountGivenMinor = group.Sum(
                        document => document.Amounts.PromotionalDiscountMinor),
                    DocumentCount = group.LongCount()
                })
            .ToListAsync(cancellationToken);

        return [.. grouped.Select(entry => new CouponRevenueBucket(
            entry.Key.PromotionCode ?? string.Empty,
            entry.Key.CurrencyCode,
            entry.RevenueMinor,
            entry.DiscountGivenMinor,
            entry.DocumentCount))];
    }

    public async Task<IReadOnlyList<CampaignRedemptionBucket>> AggregateCampaignRedemptionsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var grouped = await Redemptions(tenantId)
            .Aggregate()
            .Match(Builders<CampaignRedemption>.Filter.Eq(
                redemption => redemption.TenantId, tenantId))
            .Group(
                redemption => new { redemption.DiscountId, redemption.State },
                group => new { group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);

        return [.. grouped.Select(entry => new CampaignRedemptionBucket(
            entry.Key.DiscountId,
            entry.Key.State,
            entry.Count))];
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDiscountCodesAsync(
        string tenantId,
        IReadOnlyCollection<string> discountIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(discountIds);

        if (discountIds.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var filter = Builders<Discount>.Filter.And(
            Builders<Discount>.Filter.Eq(discount => discount.TenantId, tenantId),
            Builders<Discount>.Filter.In(discount => discount.ItemId, discountIds));

        var discounts = await Discounts(tenantId)
            .Find(filter)
            .Project(discount => new { discount.ItemId, discount.Code })
            .ToListAsync(cancellationToken);

        var codes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var discount in discounts)
        {
            codes[discount.ItemId] = discount.Code;
        }

        return codes;
    }

    private IMongoCollection<SubscriptionUsageRecord> Records(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageRecord>(
            _db, tenantId, SubscriptionCollections.UsageRecords);

    private IMongoCollection<SubscriptionDetail> Subscriptions(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionDetail>(
            _db, tenantId, SubscriptionCollections.Subscriptions);

    private IMongoCollection<SubscriptionFinancialDocument> Documents(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionFinancialDocument>(
            _db, tenantId, SubscriptionCollections.FinancialDocuments);

    private IMongoCollection<SubscriptionUsageInvoice> UsageInvoices(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageInvoice>(
            _db, tenantId, SubscriptionCollections.UsageInvoices);

    private IMongoCollection<SubscriptionUsageCurrent> UsageCurrent(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageCurrent>(
            _db, tenantId, SubscriptionCollections.UsageCurrent);

    private IMongoCollection<CampaignRedemption> Redemptions(string tenantId) =>
        SubscriptionCollections.Of<CampaignRedemption>(
            _db, tenantId, SubscriptionCollections.CampaignRedemptions);

    private IMongoCollection<Discount> Discounts(string tenantId) =>
        SubscriptionCollections.Of<Discount>(
            _db, tenantId, SubscriptionCollections.Discounts);

    /// <summary>
    /// Creates the index sets reporting reads through, once per tenant per process.
    /// </summary>
    /// <remarks>
    /// The same sets the owning repositories create, not a private set of reporting's own. Calling
    /// the shared definitions means a reporting-only tenant database still gets correct indexes,
    /// and that the two callers can never disagree about what an index is keyed on.
    /// </remarks>
    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId))
        {
            return;
        }

        await Records(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsageRecordIndexes(), cancellationToken);

        await Documents(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateFinancialDocumentIndexes(), cancellationToken);

        _indexed.TryAdd(tenantId, 0);
    }
}
