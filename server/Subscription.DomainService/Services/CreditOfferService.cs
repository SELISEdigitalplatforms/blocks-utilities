using FluentValidation;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Standalone credit offers (spec 005): one per meter per scope, priced and expired on their own
/// terms, resolved for a buyer the way plans are.
/// </summary>
public sealed class CreditOfferService : ICreditOfferService
{
    private readonly ICreditOfferRepository _offers;
    private readonly IMeterCatalogueRepository _catalogue;
    private readonly ISubscriptionContextResolver _context;
    private readonly IValidator<CreditOfferRequest> _validator;
    private readonly ISubscriptionAuditTrail? _audit;
    private readonly TimeProvider _time;

    public CreditOfferService(
        ICreditOfferRepository offers,
        IMeterCatalogueRepository catalogue,
        ISubscriptionContextResolver context,
        IValidator<CreditOfferRequest> validator,
        ISubscriptionAuditTrail? audit = null,
        TimeProvider? time = null)
    {
        _offers = offers;
        _catalogue = catalogue;
        _context = context;
        _validator = validator;
        _audit = audit;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<IReadOnlyList<CreditOfferResponse>>> ListAsync(
        string? organizationId, string? meterKey, bool? enabled, string correlationId, CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<IReadOnlyList<CreditOfferResponse>>(correlationId);

        var context = resolution.Context!;
        var scope = MeterCatalogueService.ScopeOf(organizationId, context);
        var offers = (await _offers.ListAsync(context.TenantId, scope, cancellationToken))
            .Where(offer => meterKey is null || string.Equals(offer.MeterKey, meterKey, StringComparison.Ordinal))
            .Where(offer => enabled is null || offer.Enabled == enabled)
            .ToList();

        var responses = new List<CreditOfferResponse>(offers.Count);

        foreach (var offer in offers)
        {
            responses.Add(Map(offer, await _catalogue.ResolveAsync(context.TenantId, offer.OrganizationId, offer.MeterKey, cancellationToken)));
        }

        return SubscriptionOperationResult<IReadOnlyList<CreditOfferResponse>>.Success(responses, correlationId);
    }

    public async Task<SubscriptionOperationResult<CreditOfferResponse>> GetAsync(
        string meterKey, string? organizationId, string correlationId, CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<CreditOfferResponse>(correlationId);

        var context = resolution.Context!;
        var scope = MeterCatalogueService.ScopeOf(organizationId, context);
        var offer = await _offers.ResolveAsync(context.TenantId, scope, meterKey, cancellationToken);

        return offer is null
            ? NotFound(correlationId)
            : SubscriptionOperationResult<CreditOfferResponse>.Success(
                Map(offer, await _catalogue.ResolveAsync(context.TenantId, offer.OrganizationId, meterKey, cancellationToken)),
                correlationId);
    }

    public async Task<SubscriptionOperationResult<CreditOfferResponse>> CreateAsync(
        CreditOfferRequest request, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _context.ResolveAsync(correlationId, request.OrganizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<CreditOfferResponse>(correlationId);

        var invalid = await SubscriptionValidation.CheckAsync<CreditOfferRequest, CreditOfferResponse>(
            _validator, request, "subscription_credit_offer_invalid", "The credit offer is invalid.",
            correlationId, cancellationToken);
        if (invalid is not null) return invalid;

        var context = resolution.Context!;
        var scope = MeterCatalogueService.ScopeOf(request.OrganizationId, context);
        var meterKey = request.MeterKey.Trim();

        // Resolvable in this scope: an organization override may sell a tenant-wide meter.
        var meter = await _catalogue.ResolveAsync(context.TenantId, scope, meterKey, cancellationToken);

        if (meter is null)
        {
            return SubscriptionOperationResult<CreditOfferResponse>.Failure(
                PaymentFailureKind.NotFound, "subscription_meter_catalogue_not_found",
                "No catalogue meter has this key at this scope. Add the meter first.", correlationId);
        }

        if (meter.Status == CatalogueStatus.Archived)
        {
            return SubscriptionOperationResult<CreditOfferResponse>.Failure(
                PaymentFailureKind.Conflict, "subscription_meter_catalogue_archived",
                "This meter is archived and cannot take a new offer.", correlationId);
        }

        var now = Now();
        var offer = new CreditOffer
        {
            TenantId = context.TenantId,
            OrganizationId = scope,
            MeterKey = meterKey,
            CreatedAtUtc = now
        };
        Apply(offer, request, now);

        if (!await _offers.TryCreateAsync(offer, cancellationToken))
        {
            return SubscriptionOperationResult<CreditOfferResponse>.Failure(
                PaymentFailureKind.Conflict, "subscription_credit_offer_exists",
                "An offer for this meter already exists at this scope. Edit it instead.", correlationId);
        }

        await AuditAsync("CreateCreditOffer", context, offer, correlationId, cancellationToken);

        return SubscriptionOperationResult<CreditOfferResponse>.Success(Map(offer, meter), correlationId);
    }

    public async Task<SubscriptionOperationResult<CreditOfferResponse>> UpdateAsync(
        string meterKey,
        CreditOfferRequest request,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _context.ResolveAsync(correlationId, organizationId, cancellationToken);
        if (!resolution.IsSuccess) return resolution.ToFailure<CreditOfferResponse>(correlationId);

        // The key comes from the route; the validator still wants one.
        request.MeterKey = meterKey;

        var invalid = await SubscriptionValidation.CheckAsync<CreditOfferRequest, CreditOfferResponse>(
            _validator, request, "subscription_credit_offer_invalid", "The credit offer is invalid.",
            correlationId, cancellationToken);
        if (invalid is not null) return invalid;

        var context = resolution.Context!;
        var scope = MeterCatalogueService.ScopeOf(organizationId, context);
        var offer = await _offers.FindAtScopeAsync(context.TenantId, scope, meterKey, cancellationToken);

        if (offer is null)
        {
            return NotFound(correlationId);
        }

        var expectedVersion = offer.Version;
        Apply(offer, request, Now());

        if (!await _offers.TryUpdateAsync(offer, expectedVersion, cancellationToken))
        {
            return SubscriptionOperationResult<CreditOfferResponse>.Failure(
                PaymentFailureKind.Conflict, "subscription_credit_offer_changed",
                "This offer changed while you were editing it. Reload it and apply your changes again.",
                correlationId);
        }

        await AuditAsync("UpdateCreditOffer", context, offer, correlationId, cancellationToken);

        return SubscriptionOperationResult<CreditOfferResponse>.Success(
            Map(offer, await _catalogue.ResolveAsync(context.TenantId, scope, meterKey, cancellationToken)),
            correlationId);
    }

    private static void Apply(CreditOffer offer, CreditOfferRequest request, DateTime now)
    {
        offer.Enabled = request.Enabled;
        offer.UnitPrices = request.UnitPrices
            .Select(price => new MeterTopUpPrice
            {
                CurrencyCode = price.CurrencyCode.Trim().ToUpperInvariant(),
                UnitAmountMinor = price.UnitAmountMinor
            })
            .ToList();
        offer.MinQuantity = request.MinQuantity;
        offer.MaxQuantity = request.MaxQuantity;
        offer.ExpiresAfter = request.ExpiresAfter is { } expiry
            ? new CreditExpiryDuration { Count = expiry.Count, Unit = expiry.Unit }
            : null;
        offer.TaxRateBasisPoints = request.TaxRateBasisPoints;
        offer.TaxMode = request.TaxMode;
        offer.SpendOrder = request.SpendOrder;
        offer.LastUpdatedAtUtc = now;
    }

    private static SubscriptionOperationResult<CreditOfferResponse> NotFound(string correlationId) =>
        SubscriptionOperationResult<CreditOfferResponse>.Failure(
            PaymentFailureKind.NotFound, "subscription_credit_offer_not_found",
            "No credit offer exists for this meter at this scope.", correlationId);

    private Task AuditAsync(
        string operation,
        SubscriptionContext context,
        CreditOffer offer,
        string correlationId,
        CancellationToken cancellationToken) =>
        _audit?.RecordAsync(new SubscriptionAuditEvent
        {
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            AggregateType = "CreditOffer",
            AggregateId = offer.ItemId,
            AggregateCode = offer.MeterKey,
            OperationId = $"credit-offer:{offer.ItemId}:{operation}:{correlationId}",
            CorrelationId = correlationId,
            Operation = operation,
            Stage = "Catalogue",
            Outcome = "Succeeded",
            Source = "Api",
            ActorId = context.ActorId,
            UserId = context.UserId,
            OccurredAtUtc = Now()
        }, cancellationToken) ?? Task.CompletedTask;

    internal static CreditOfferResponse Map(CreditOffer offer, MeterCatalogueEntry? meter) => new()
    {
        OfferId = offer.ItemId,
        OrganizationId = offer.OrganizationId,
        MeterKey = offer.MeterKey,
        DisplayName = meter?.DisplayName ?? offer.MeterKey,
        UnitLabel = meter?.UnitLabel ?? string.Empty,
        Enabled = offer.Enabled,
        UnitPrices = offer.UnitPrices
            .Select(price => new PlanMeterTopUpPriceResponse
            {
                CurrencyCode = price.CurrencyCode,
                UnitAmountMinor = price.UnitAmountMinor
            })
            .ToList(),
        MinQuantity = offer.MinQuantity,
        MaxQuantity = offer.MaxQuantity,
        ExpiresAfter = offer.ExpiresAfter is { } expiry
            ? new CreditExpiryDurationResponse { Count = expiry.Count, Unit = expiry.Unit.ToString() }
            : null,
        TaxRateBasisPoints = offer.TaxRateBasisPoints,
        TaxMode = offer.TaxMode?.ToString(),
        SpendOrder = offer.SpendOrder.ToString(),
        CreatedAtUtc = offer.CreatedAtUtc,
        LastUpdatedAtUtc = offer.LastUpdatedAtUtc,
        Version = offer.Version
    };

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
}
