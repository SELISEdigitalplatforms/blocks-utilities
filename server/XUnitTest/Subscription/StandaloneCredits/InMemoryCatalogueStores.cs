using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Subscription.StandaloneCredits;

/// <summary>Behaves like the Mongo catalogue: one key per scope, version-guarded updates, override-then-tenant resolution.</summary>
internal sealed class InMemoryMeterCatalogue : IMeterCatalogueRepository
{
    public List<MeterCatalogueEntry> All { get; } = [];

    public Task<bool> TryCreateAsync(MeterCatalogueEntry entry, CancellationToken cancellationToken)
    {
        if (All.Exists(existing => existing.OrganizationId == entry.OrganizationId && existing.MeterKey == entry.MeterKey))
        {
            return Task.FromResult(false);
        }

        All.Add(entry);
        return Task.FromResult(true);
    }

    public async Task<MeterCatalogueEntry?> ResolveAsync(
        string tenantId, string? organizationId, string meterKey, CancellationToken cancellationToken) =>
        (organizationId is null ? null : await FindAtScopeAsync(tenantId, organizationId, meterKey, cancellationToken))
        ?? await FindAtScopeAsync(tenantId, null, meterKey, cancellationToken);

    public Task<MeterCatalogueEntry?> FindAtScopeAsync(
        string tenantId, string? organizationId, string meterKey, CancellationToken cancellationToken) =>
        Task.FromResult(All.Find(entry => entry.OrganizationId == organizationId && entry.MeterKey == meterKey));

    public Task<IReadOnlyList<MeterCatalogueEntry>> ListAsync(
        string tenantId, string? organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MeterCatalogueEntry>>(
            [.. All.Where(entry => entry.OrganizationId is null || entry.OrganizationId == organizationId)]);

    public Task<bool> TryUpdateAsync(MeterCatalogueEntry entry, long expectedVersion, CancellationToken cancellationToken)
    {
        var stored = All.Find(existing => existing.ItemId == entry.ItemId);

        if (stored is null || stored.Version != expectedVersion)
        {
            return Task.FromResult(false);
        }

        entry.Version = expectedVersion + 1;
        All[All.IndexOf(stored)] = entry;
        return Task.FromResult(true);
    }
}

internal sealed class InMemoryCreditOffers : ICreditOfferRepository
{
    public List<CreditOffer> All { get; } = [];

    public Task<bool> TryCreateAsync(CreditOffer offer, CancellationToken cancellationToken)
    {
        if (All.Exists(existing => existing.OrganizationId == offer.OrganizationId && existing.MeterKey == offer.MeterKey))
        {
            return Task.FromResult(false);
        }

        All.Add(offer);
        return Task.FromResult(true);
    }

    public async Task<CreditOffer?> ResolveAsync(
        string tenantId, string? organizationId, string meterKey, CancellationToken cancellationToken) =>
        (organizationId is null ? null : await FindAtScopeAsync(tenantId, organizationId, meterKey, cancellationToken))
        ?? await FindAtScopeAsync(tenantId, null, meterKey, cancellationToken);

    public Task<CreditOffer?> FindAtScopeAsync(
        string tenantId, string? organizationId, string meterKey, CancellationToken cancellationToken) =>
        Task.FromResult(All.Find(offer => offer.OrganizationId == organizationId && offer.MeterKey == meterKey));

    public Task<IReadOnlyList<CreditOffer>> ListAsync(
        string tenantId, string? organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CreditOffer>>(
            [.. All.Where(offer => offer.OrganizationId is null || offer.OrganizationId == organizationId)]);

    public Task<bool> TryUpdateAsync(CreditOffer offer, long expectedVersion, CancellationToken cancellationToken)
    {
        var stored = All.Find(existing => existing.ItemId == offer.ItemId);

        if (stored is null || stored.Version != expectedVersion)
        {
            return Task.FromResult(false);
        }

        offer.Version = expectedVersion + 1;
        All[All.IndexOf(stored)] = offer;
        return Task.FromResult(true);
    }
}
