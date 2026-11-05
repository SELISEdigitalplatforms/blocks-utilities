using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// The meter catalogue (spec 005): one entry per key per scope, resolved the way plans and
/// discounts are — an organization's own entry wins, otherwise the tenant's.
/// </summary>
/// <remarks>
/// The scope a request writes is decided the way the discount catalogue decides it: a named
/// organization is honoured only for the platform console, and a request naming none writes the
/// tenant-wide entry. The resolver already enforces who may name whom, so anyone else naming a
/// stranger's organization is scoped to their own.
/// </remarks>
public sealed class MeterCatalogueService : IMeterCatalogueService
{
    private readonly IMeterCatalogueRepository _entries;
    private readonly ISubscriptionContextResolver _context;
    private readonly IValidator<CreateMeterCatalogueEntryRequest> _createValidator;
    private readonly IValidator<UpdateMeterCatalogueEntryRequest> _updateValidator;
    private readonly ISubscriptionAuditTrail? _audit;
    private readonly ILogger<MeterCatalogueService> _logger;
    private readonly TimeProvider _time;

    public MeterCatalogueService(
        IMeterCatalogueRepository entries,
        ISubscriptionContextResolver context,
        IValidator<CreateMeterCatalogueEntryRequest> createValidator,
        IValidator<UpdateMeterCatalogueEntryRequest> updateValidator,
        ILogger<MeterCatalogueService> logger,
        ISubscriptionAuditTrail? audit = null,
        TimeProvider? time = null)
    {
        _entries = entries;
        _context = context;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
        _audit = audit;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<IReadOnlyList<MeterCatalogueEntryResponse>>> ListAsync(
        string? organizationId, string correlationId, CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<IReadOnlyList<MeterCatalogueEntryResponse>>(correlationId);

        var entries = await _entries.ListAsync(
            resolution.Context!.TenantId, ScopeOf(organizationId, resolution.Context), cancellationToken);

        return SubscriptionOperationResult<IReadOnlyList<MeterCatalogueEntryResponse>>.Success(
            [.. entries.Select(Map)], correlationId);
    }

    public async Task<SubscriptionOperationResult<MeterCatalogueEntryResponse>> GetAsync(
        string meterKey, string? organizationId, string correlationId, CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<MeterCatalogueEntryResponse>(correlationId);

        var entry = await _entries.ResolveAsync(
            resolution.Context!.TenantId, ScopeOf(organizationId, resolution.Context), meterKey, cancellationToken);

        return entry is null
            ? NotFound(correlationId)
            : SubscriptionOperationResult<MeterCatalogueEntryResponse>.Success(Map(entry), correlationId);
    }

    public async Task<SubscriptionOperationResult<MeterCatalogueEntryResponse>> CreateAsync(
        CreateMeterCatalogueEntryRequest request, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _context.ResolveAsync(correlationId, request.OrganizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<MeterCatalogueEntryResponse>(correlationId);

        var invalid = await SubscriptionValidation.CheckAsync<CreateMeterCatalogueEntryRequest, MeterCatalogueEntryResponse>(
            _createValidator, request, "subscription_meter_catalogue_invalid",
            "The catalogue meter is invalid.", correlationId, cancellationToken);
        if (invalid is not null) return invalid;

        var context = resolution.Context!;
        var now = Now();
        var entry = new MeterCatalogueEntry
        {
            TenantId = context.TenantId,
            OrganizationId = ScopeOf(request.OrganizationId, context),
            MeterKey = request.MeterKey.Trim(),
            DisplayName = request.DisplayName.Trim(),
            UnitLabel = request.UnitLabel.Trim(),
            Aggregation = request.Aggregation,
            QuantityScale = request.QuantityScale,
            CreatedAtUtc = now,
            LastUpdatedAtUtc = now
        };

        if (!await _entries.TryCreateAsync(entry, cancellationToken))
        {
            return SubscriptionOperationResult<MeterCatalogueEntryResponse>.Failure(
                PaymentFailureKind.Conflict, "subscription_meter_catalogue_exists",
                "A meter with this key already exists at this scope.", correlationId);
        }

        await AuditAsync("CreateCatalogueMeter", context, entry, correlationId, cancellationToken);

        return SubscriptionOperationResult<MeterCatalogueEntryResponse>.Success(Map(entry), correlationId);
    }

    public async Task<SubscriptionOperationResult<MeterCatalogueEntryResponse>> UpdateAsync(
        string meterKey,
        UpdateMeterCatalogueEntryRequest request,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<MeterCatalogueEntryResponse>(correlationId);

        var invalid = await SubscriptionValidation.CheckAsync<UpdateMeterCatalogueEntryRequest, MeterCatalogueEntryResponse>(
            _updateValidator, request, "subscription_meter_catalogue_invalid",
            "The catalogue meter is invalid.", correlationId, cancellationToken);
        if (invalid is not null) return invalid;

        var context = resolution.Context!;

        // Exactly the scope named, never the resolved fallback: editing "the tenant's entry as seen
        // from this organization" must not quietly rewrite the tenant's entry.
        var entry = await _entries.FindAtScopeAsync(
            context.TenantId, ScopeOf(organizationId, context), meterKey, cancellationToken);

        if (entry is null)
        {
            return NotFound(correlationId);
        }

        if (entry.Status == CatalogueStatus.Archived && request.Status == CatalogueStatus.Archived)
        {
            return SubscriptionOperationResult<MeterCatalogueEntryResponse>.Failure(
                PaymentFailureKind.Conflict, "subscription_meter_catalogue_archived",
                "This meter is already archived.", correlationId);
        }

        var expectedVersion = entry.Version;
        entry.DisplayName = request.DisplayName.Trim();
        entry.UnitLabel = request.UnitLabel.Trim();
        entry.Aggregation = request.Aggregation;
        entry.QuantityScale = request.QuantityScale;
        entry.Status = request.Status;
        entry.LastUpdatedAtUtc = Now();

        if (!await _entries.TryUpdateAsync(entry, expectedVersion, cancellationToken))
        {
            return SubscriptionOperationResult<MeterCatalogueEntryResponse>.Failure(
                PaymentFailureKind.Conflict, "subscription_meter_catalogue_changed",
                "This meter changed while you were editing it. Reload it and apply your changes again.",
                correlationId);
        }

        await AuditAsync(
            request.Status == CatalogueStatus.Archived ? "ArchiveCatalogueMeter" : "UpdateCatalogueMeter",
            context, entry, correlationId, cancellationToken);

        return SubscriptionOperationResult<MeterCatalogueEntryResponse>.Success(Map(entry), correlationId);
    }

    public async Task EnsureEntriesAsync(
        string tenantId,
        string? organizationId,
        IReadOnlyCollection<PlanMeter> meters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(meters);

        foreach (var meter in meters)
        {
            try
            {
                if (await _entries.ResolveAsync(tenantId, organizationId, meter.MeterKey, cancellationToken) is not null)
                {
                    continue;
                }

                var now = Now();

                // A race with another save of the same key is a duplicate-key refusal, which is the
                // entry existing: exactly what was wanted.
                await _entries.TryCreateAsync(
                    new MeterCatalogueEntry
                    {
                        TenantId = tenantId,
                        OrganizationId = organizationId,
                        MeterKey = meter.MeterKey,
                        DisplayName = meter.DisplayName,
                        UnitLabel = meter.UnitLabel,
                        Aggregation = meter.Aggregation,
                        QuantityScale = meter.QuantityScale,
                        CreatedAtUtc = now,
                        LastUpdatedAtUtc = now
                    },
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "A plan meter could not be added to the catalogue; the next plan save will try again TenantId={TenantId} MeterKey={MeterKey}",
                    PaymentLogValue.Id(tenantId),
                    PaymentLogValue.Label(meter.MeterKey));
            }
        }
    }

    /// <summary>
    /// Null (the tenant) when the request named no organization, otherwise the organization the
    /// resolver settled on — never the raw request value, for the reason the discount catalogue gives.
    /// </summary>
    internal static string? ScopeOf(string? requestedOrganizationId, SubscriptionContext context) =>
        string.IsNullOrWhiteSpace(requestedOrganizationId) ? null : context.OrganizationId;

    private static SubscriptionOperationResult<MeterCatalogueEntryResponse> NotFound(string correlationId) =>
        SubscriptionOperationResult<MeterCatalogueEntryResponse>.Failure(
            PaymentFailureKind.NotFound, "subscription_meter_catalogue_not_found",
            "No catalogue meter has this key at this scope.", correlationId);

    private Task AuditAsync(
        string operation,
        SubscriptionContext context,
        MeterCatalogueEntry entry,
        string correlationId,
        CancellationToken cancellationToken) =>
        _audit?.RecordAsync(new SubscriptionAuditEvent
        {
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            AggregateType = "MeterCatalogueEntry",
            AggregateId = entry.ItemId,
            AggregateCode = entry.MeterKey,
            OperationId = $"meter-catalogue:{entry.ItemId}:{operation}:{correlationId}",
            CorrelationId = correlationId,
            Operation = operation,
            Stage = "Catalogue",
            Outcome = "Succeeded",
            Source = "Api",
            ActorId = context.ActorId,
            UserId = context.UserId,
            OccurredAtUtc = Now()
        }, cancellationToken) ?? Task.CompletedTask;

    internal static MeterCatalogueEntryResponse Map(MeterCatalogueEntry entry) => new()
    {
        EntryId = entry.ItemId,
        OrganizationId = entry.OrganizationId,
        MeterKey = entry.MeterKey,
        DisplayName = entry.DisplayName,
        UnitLabel = entry.UnitLabel,
        Aggregation = entry.Aggregation.ToString(),
        QuantityScale = entry.QuantityScale,
        Status = entry.Status.ToString(),
        CreatedAtUtc = entry.CreatedAtUtc,
        LastUpdatedAtUtc = entry.LastUpdatedAtUtc,
        Version = entry.Version
    };

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
}
