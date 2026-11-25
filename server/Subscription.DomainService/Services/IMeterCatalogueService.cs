using Subscription.DomainService.Entities;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>Authoring and reading the meter catalogue. See spec 005.</summary>
public interface IMeterCatalogueService
{
    Task<SubscriptionOperationResult<IReadOnlyList<MeterCatalogueEntryResponse>>> ListAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<MeterCatalogueEntryResponse>> GetAsync(
        string meterKey,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<MeterCatalogueEntryResponse>> CreateAsync(
        CreateMeterCatalogueEntryRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<MeterCatalogueEntryResponse>> UpdateAsync(
        string meterKey,
        UpdateMeterCatalogueEntryRequest request,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds a catalogue entry for every meter of a saved plan whose key the plan's scope cannot
    /// resolve yet, copying the plan's identity fields. Never fails the save: a missing entry is
    /// repaired by the next save, a failed plan is not.
    /// </summary>
    Task EnsureEntriesAsync(
        string tenantId,
        string? organizationId,
        IReadOnlyCollection<PlanMeter> meters,
        CancellationToken cancellationToken);

    /// <summary>
    /// Who created or changed this meter, and when, newest first. <c>limit</c> defaults to 100,
    /// at most 500. Not found under the same rule as <see cref="GetAsync"/>.
    /// </summary>
    Task<SubscriptionOperationResult<IReadOnlyList<SubscriptionAuditEventResponse>>> GetAuditAsync(
        string meterKey, string? organizationId, int limit, string correlationId, CancellationToken cancellationToken);
}
