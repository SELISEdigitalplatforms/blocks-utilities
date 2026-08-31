using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionCatalogueRepository : ISubscriptionCatalogueRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionCatalogueRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Plans(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreatePlanIndexes(),
            cancellationToken);

        await Prices(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreatePriceIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<bool> TryCreatePlanAsync(
        Plan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        try
        {
            await EnsureIndexesAsync(plan.TenantId, cancellationToken);

            await Plans(plan.TenantId)
                .InsertOneAsync(plan, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<Plan?> FindPlanByCodeAsync(
        string tenantId,
        string? organizationId,
        string code,
        CancellationToken cancellationToken)
    {
        var plans = Plans(tenantId);
        var baseFilter = Builders<Plan>.Filter.And(
            Builders<Plan>.Filter.Eq(plan => plan.TenantId, tenantId),
            Builders<Plan>.Filter.Eq(plan => plan.Code, code),
            Builders<Plan>.Filter.Eq(plan => plan.Status, CatalogueStatus.Active));

        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            var owned = await plans
                .Find(Builders<Plan>.Filter.And(
                    baseFilter,
                    Builders<Plan>.Filter.Eq(plan => plan.OrganizationId, organizationId)))
                .FirstOrDefaultAsync(cancellationToken);

            if (owned is not null)
            {
                return owned;
            }
        }

        // The tenant's own catalogue, serving every organization without one of its own.
        return await plans
            .Find(Builders<Plan>.Filter.And(
                baseFilter,
                Builders<Plan>.Filter.Eq(plan => plan.OrganizationId, null)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Plan?> GetPlanAsync(
        string tenantId,
        string planId,
        CancellationToken cancellationToken) =>
        await Plans(tenantId)
            .Find(Builders<Plan>.Filter.And(
                Builders<Plan>.Filter.Eq(plan => plan.TenantId, tenantId),
                Builders<Plan>.Filter.Eq(plan => plan.ItemId, planId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Plan>> ListPlansAsync(
        string tenantId,
        string? organizationId,
        PlanCatalogueFilter filter,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var visible = VisibleToOrganization(organizationId);

        // Draft is absent from every one of these views, which is what it has always been: an
        // internal state no catalogue has shown. The wanted statuses are named rather than
        // Archived excluded, so a fourth status could never appear here by default.
        var statuses = filter switch
        {
            PlanCatalogueFilter.Archived =>
                Builders<Plan>.Filter.Eq(plan => plan.Status, CatalogueStatus.Archived),
            PlanCatalogueFilter.All =>
                Builders<Plan>.Filter.In(
                    plan => plan.Status,
                    new[] { CatalogueStatus.Active, CatalogueStatus.Archived }),
            _ => Builders<Plan>.Filter.Eq(plan => plan.Status, CatalogueStatus.Active)
        };

        var plans = await Plans(tenantId)
            .Find(Builders<Plan>.Filter.And(
                Builders<Plan>.Filter.Eq(plan => plan.TenantId, tenantId),
                statuses,
                visible))
            .ToListAsync(cancellationToken);

        // Only the sellable half is resolved. An organization's own plan hides the tenant's of the
        // same code, matching what FindPlanByCodeAsync would resolve — showing both would offer a
        // choice that subscribing cannot honour.
        //
        // Archived plans are never collapsed, and are never allowed to collapse an active one
        // either: under All, an organization's archived "pro" and the tenant's active "pro" are
        // two separate records a reader needs to tell apart, and hiding the active one behind the
        // archived one would misreport what is on sale.
        var resolved = plans
            .Where(plan => plan.Status == CatalogueStatus.Active)
            .GroupBy(plan => plan.Code, StringComparer.Ordinal)
            .Select(group =>
                group.FirstOrDefault(plan => plan.OrganizationId is not null) ??
                group.First());

        var archived = plans.Where(plan => plan.Status == CatalogueStatus.Archived);

        return resolved
            .Concat(archived)
            .OrderBy(plan => plan.Code, StringComparer.Ordinal)
            .ThenBy(plan => plan.Status == CatalogueStatus.Archived ? 1 : 0)
            .ThenByDescending(plan => plan.LastUpdatedDateUtc)
            .ToList();
    }

    public async Task<Plan?> FindArchivedPlanByCodeAsync(
        string tenantId,
        string? organizationId,
        string code,
        CancellationToken cancellationToken)
    {
        var plans = Plans(tenantId);
        var baseFilter = Builders<Plan>.Filter.And(
            Builders<Plan>.Filter.Eq(plan => plan.TenantId, tenantId),
            Builders<Plan>.Filter.Eq(plan => plan.Code, code),
            Builders<Plan>.Filter.Eq(plan => plan.Status, CatalogueStatus.Archived));

        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            var owned = await plans
                .Find(Builders<Plan>.Filter.And(
                    baseFilter,
                    Builders<Plan>.Filter.Eq(plan => plan.OrganizationId, organizationId)))
                .FirstOrDefaultAsync(cancellationToken);

            if (owned is not null)
            {
                return owned;
            }
        }

        return await plans
            .Find(Builders<Plan>.Filter.And(
                baseFilter,
                Builders<Plan>.Filter.Eq(plan => plan.OrganizationId, null)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryArchivePlanAsync(
        string tenantId,
        string planId,
        int expectedVersion,
        DateTime archivedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Plans(tenantId).UpdateOneAsync(
            Builders<Plan>.Filter.And(
                Builders<Plan>.Filter.Eq(plan => plan.TenantId, tenantId),
                Builders<Plan>.Filter.Eq(plan => plan.ItemId, planId),

                // Active only: a draft was never on a menu, and an already-archived plan must not
                // be written twice, so the caller can report a repeat as the no-op it is.
                Builders<Plan>.Filter.Eq(plan => plan.Status, CatalogueStatus.Active),

                // The version just read. An unrelated edit landing in between moves it on, and
                // this archive is refused rather than applied to terms nobody reviewed.
                Builders<Plan>.Filter.Eq(plan => plan.Version, expectedVersion)),
            Builders<Plan>.Update
                .Set(plan => plan.Status, CatalogueStatus.Archived)
                .Set(plan => plan.LastUpdatedDateUtc, archivedAtUtc)
                .Inc(plan => plan.Version, 1),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    private static FilterDefinition<Plan> VisibleToOrganization(string? organizationId) =>
        string.IsNullOrWhiteSpace(organizationId)
            ? Builders<Plan>.Filter.Eq(plan => plan.OrganizationId, null)
            : Builders<Plan>.Filter.Or(
                Builders<Plan>.Filter.Eq(plan => plan.OrganizationId, organizationId),
                Builders<Plan>.Filter.Eq(plan => plan.OrganizationId, null));

    public async Task<Plan?> FindSuccessorPlanAsync(
        string tenantId,
        string predecessorPlanId,
        CancellationToken cancellationToken) =>
        await Plans(tenantId)
            .Find(Builders<Plan>.Filter.And(
                Builders<Plan>.Filter.Eq(plan => plan.TenantId, tenantId),
                Builders<Plan>.Filter.Eq(plan => plan.PredecessorPlanId, predecessorPlanId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryUpdatePlanAsync(
        string tenantId,
        string planId,
        int expectedVersion,
        Plan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var filter = Builders<Plan>.Filter.And(
            Builders<Plan>.Filter.Eq(existing => existing.TenantId, tenantId),
            Builders<Plan>.Filter.Eq(existing => existing.ItemId, planId),
            Builders<Plan>.Filter.Eq(existing => existing.Version, expectedVersion));

        var update = Builders<Plan>.Update
            .Set(existing => existing.DisplayName, plan.DisplayName)
            .Set(existing => existing.Description, plan.Description)
            .Set(existing => existing.Status, plan.Status)
            .Set(existing => existing.FeaturesJson, plan.FeaturesJson)
            .Set(existing => existing.Entitlements, plan.Entitlements)
            .Set(existing => existing.Meters, plan.Meters)
            .Set(existing => existing.QuantityItems, plan.QuantityItems)
            .Set(existing => existing.TrialDays, plan.TrialDays)
            .Set(existing => existing.TrialDurationKind, plan.TrialDurationKind)
            .Set(existing => existing.TrialDurationCount, plan.TrialDurationCount)
            .Set(existing => existing.TrialRequiresPaymentMethod, plan.TrialRequiresPaymentMethod)
            .Set(existing => existing.RequirePaymentMethodUpfront, plan.RequirePaymentMethodUpfront)
            .Set(existing => existing.TrialGrants, plan.TrialGrants)
            .Set(existing => existing.LastUpdatedDateUtc, DateTime.UtcNow)
            .Inc(existing => existing.Version, 1);

        var result = await Plans(tenantId).UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryCreatePriceAsync(
        Price price,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(price);

        try
        {
            await EnsureIndexesAsync(price.TenantId, cancellationToken);

            await Prices(price.TenantId)
                .InsertOneAsync(price, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<Price?> GetPriceAsync(
        string tenantId,
        string priceId,
        CancellationToken cancellationToken) =>
        await Prices(tenantId)
            .Find(Builders<Price>.Filter.And(
                Builders<Price>.Filter.Eq(price => price.TenantId, tenantId),
                Builders<Price>.Filter.Eq(price => price.ItemId, priceId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Price>> ListPricesAsync(
        string tenantId,
        string planId,
        CancellationToken cancellationToken) =>
        await Prices(tenantId)
            .Find(Builders<Price>.Filter.And(
                Builders<Price>.Filter.Eq(price => price.TenantId, tenantId),
                Builders<Price>.Filter.Eq(price => price.PlanId, planId),
                Builders<Price>.Filter.Eq(price => price.Status, CatalogueStatus.Active)))
            .ToListAsync(cancellationToken);

    public async Task<bool> TryArchivePriceAsync(
        string tenantId,
        string priceId,
        DateTime archivedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Prices(tenantId).UpdateOneAsync(
            Builders<Price>.Filter.And(
                Builders<Price>.Filter.Eq(price => price.TenantId, tenantId),
                Builders<Price>.Filter.Eq(price => price.ItemId, priceId),

                // Only an active price can be archived, so a repeated call is reported rather
                // than silently succeeding, and a draft is not quietly retired before it sold.
                Builders<Price>.Filter.Eq(price => price.Status, CatalogueStatus.Active)),
            Builders<Price>.Update
                .Set(price => price.Status, CatalogueStatus.Archived)
                .Set(price => price.LastUpdatedDateUtc, archivedAtUtc)
                .Inc(price => price.Version, 1),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryUpdatePriceTaxAsync(
        string tenantId,
        string priceId,
        int expectedVersion,
        int? taxRateBasisPoints,
        TaxMode? taxMode,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Prices(tenantId).UpdateOneAsync(
            Builders<Price>.Filter.And(
                Builders<Price>.Filter.Eq(price => price.TenantId, tenantId),
                Builders<Price>.Filter.Eq(price => price.ItemId, priceId),
                Builders<Price>.Filter.Eq(price => price.Version, expectedVersion),
                Builders<Price>.Filter.Eq(price => price.Status, CatalogueStatus.Active)),
            Builders<Price>.Update
                .Set(price => price.TaxRateBasisPoints, taxRateBasisPoints)
                .Set(price => price.TaxMode, taxRateBasisPoints > 0 ? taxMode : null)
                .Set(price => price.LastUpdatedDateUtc, updatedAtUtc)
                .Inc(price => price.Version, 1),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryUpdatePriceAutomaticDiscountAsync(
        string tenantId,
        string priceId,
        int expectedVersion,
        int? automaticDiscountBasisPoints,
        AutomaticDiscountCombination? combination,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Prices(tenantId).UpdateOneAsync(
            Builders<Price>.Filter.And(
                Builders<Price>.Filter.Eq(price => price.TenantId, tenantId),
                Builders<Price>.Filter.Eq(price => price.ItemId, priceId),
                Builders<Price>.Filter.Eq(price => price.Version, expectedVersion),
                Builders<Price>.Filter.Eq(price => price.Status, CatalogueStatus.Active)),
            Builders<Price>.Update
                .Set(price => price.AutomaticDiscountBasisPoints, automaticDiscountBasisPoints)
                // Cleared with the rate it describes, so a price with no discount cannot carry a
                // stale answer to how that discount combines with a band.
                .Set(
                    price => price.QuantityDiscountCombination,
                    automaticDiscountBasisPoints > 0 ? combination : null)
                .Set(price => price.LastUpdatedDateUtc, updatedAtUtc)
                .Inc(price => price.Version, 1),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TrySetPriceMirrorAsync(
        string tenantId,
        string priceId,
        ProviderPriceMirror mirror,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mirror);

        // Replacing any existing mirror for the same provider keeps this idempotent: mirroring
        // twice leaves one entry, not two competing identifiers for the same price.
        var filter = Builders<Price>.Filter.And(
            Builders<Price>.Filter.Eq(price => price.TenantId, tenantId),
            Builders<Price>.Filter.Eq(price => price.ItemId, priceId));

        await Prices(tenantId).UpdateOneAsync(
            filter,
            Builders<Price>.Update.PullFilter(
                price => price.ProviderMirrors,
                existing => existing.ProviderName == mirror.ProviderName),
            cancellationToken: cancellationToken);

        var result = await Prices(tenantId).UpdateOneAsync(
            filter,
            Builders<Price>.Update
                .Push(price => price.ProviderMirrors, mirror)
                .Set(price => price.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    private IMongoCollection<Plan> Plans(string tenantId) =>
        SubscriptionCollections.Of<Plan>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.Plans);

    private IMongoCollection<Price> Prices(string tenantId) =>
        SubscriptionCollections.Of<Price>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.Prices);
}
