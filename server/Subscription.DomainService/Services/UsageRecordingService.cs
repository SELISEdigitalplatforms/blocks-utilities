using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Recording what an organization used.
/// </summary>
/// <remarks>
/// The ledger is written before the counter, deliberately. A crash between the two leaves the
/// counter under-counting, which the repair sweep can correct from the ledger; the other order
/// would over-count with nothing left to prove it, and the customer would be billed for it.
/// </remarks>
public sealed class UsageRecordingService : IUsageRecordingService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IUsageThresholdEvaluator _thresholds;
    private readonly IValidator<RecordUsageRequest> _validator;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<UsageRecordingService> _logger;
    private readonly TimeProvider _time;

    public UsageRecordingService(
        ISubscriptionRepository subscriptions,
        ISubscriptionUsageRepository usage,
        ISubscriptionContextResolver contextResolver,
        IUsageThresholdEvaluator thresholds,
        IValidator<RecordUsageRequest> validator,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<UsageRecordingService> logger,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _usage = usage;
        _contextResolver = contextResolver;
        _thresholds = thresholds;
        _validator = validator;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<UsageResponse>> RecordAsync(
        RecordUsageRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<UsageResponse>(correlationId);
        }

        var invalid = await SubscriptionValidation.CheckAsync<RecordUsageRequest, UsageResponse>(
            _validator,
            request,
            "subscription_usage_invalid",
            "The usage record is invalid.",
            correlationId,
            cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetLiveAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        if (subscription is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "This organization has no active subscription.",
                correlationId);
        }

        var meter = FindMeter(subscription, request.MeterKey);

        if (meter is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_meter_not_found",
                "The plan does not define this meter.",
                correlationId);
        }

        if (meter.Aggregation != MeterAggregation.Sum)
        {
            // Defined in the enum so it need not widen later, but refused explicitly rather
            // than silently treated as a sum, which would bill the wrong figure.
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_meter_aggregation_unsupported",
                "Only summed meters can be recorded in this release.",
                correlationId);
        }

        if (request.Quantity < 0 && meter.ResetPolicy != MeterResetPolicy.Never)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_usage_reduction_not_allowed",
                "Only a never-reset capacity meter accepts negative usage adjustments.",
                correlationId);
        }

        var occurredAt = request.OccurredAtUtc ?? _time.GetUtcNow().UtcDateTime;

        if (!MeterPeriodResolver.TryGetPeriod(
                subscription,
                meter,
                occurredAt,
                out var period))
        {
            return Failure(
                PaymentFailureKind.Unavailable,
                "subscription_schedule_unavailable",
                "The usage period could not be determined.",
                correlationId);
        }

        return await ApplyAsync(
            request,
            context,
            subscription,
            meter,
            period,
            occurredAt,
            correlationId,
            cancellationToken);
    }

    public async Task<SubscriptionOperationResult<IReadOnlyList<UsageResponse>>> GetCurrentUsageAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            organizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<IReadOnlyList<UsageResponse>>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetLiveAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        if (subscription is null)
        {
            return SubscriptionOperationResult<IReadOnlyList<UsageResponse>>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "This organization has no active subscription.",
                correlationId);
        }

        var now = _time.GetUtcNow().UtcDateTime;

        var responses = new List<UsageResponse>();

        foreach (var meter in subscription.Plan.Meters)
        {
            if (!MeterPeriodResolver.TryGetPeriod(subscription, meter, now, out var period))
            {
                return SubscriptionOperationResult<IReadOnlyList<UsageResponse>>.Failure(
                    PaymentFailureKind.Unavailable,
                    "subscription_schedule_unavailable",
                    "The usage period could not be determined.",
                    correlationId);
            }

            var counter = await _usage.GetCounterAsync(
                context.TenantId,
                SubscriptionUsageCounter.CreateId(subscription.ItemId, meter.MeterKey, period.Key),
                cancellationToken);

            responses.Add(Describe(
                meter,
                period,
                counter?.Balance ?? 0,
                AllowanceFor(subscription, meter),
                allowed: true,
                replayed: false));
        }

        return SubscriptionOperationResult<IReadOnlyList<UsageResponse>>.Success(
            responses,
            correlationId);
    }

    private async Task<SubscriptionOperationResult<UsageResponse>> ApplyAsync(
        RecordUsageRequest request,
        SubscriptionContext context,
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        DateTime occurredAt,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var allowance = AllowanceFor(subscription, meter);

        var record = new SubscriptionUsageRecord
        {
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            SubscriptionId = subscription.ItemId,
            MeterKey = meter.MeterKey,
            PeriodKey = period.Key,
            EntryType = UsageEntryType.Consumption,
            Delta = request.Quantity,
            IdempotencyKey = request.IdempotencyKey,
            OccurredAtUtc = occurredAt,
            Metadata = request.Metadata,
            RecordedByUserId = context.UserId,
            CorrelationId = correlationId
        };

        if (!await _usage.TryAppendRecordAsync(record, cancellationToken))
        {
            return await ReplayAsync(
                subscription,
                meter,
                period,
                allowance,
                correlationId,
                cancellationToken);
        }

        var counter = await _usage.ApplyDeltaAsync(
            SeedFor(context, subscription, meter, period, allowance),
            request.Quantity,
            cancellationToken);

        var withinAllowance = counter.Balance <= allowance;

        if (counter.Balance < 0)
        {
            return await RefuseAsync(
                record,
                context,
                subscription,
                meter,
                period,
                allowance,
                correlationId,
                cancellationToken);
        }

        if (request.Enforce && !withinAllowance && !meter.OverageAllowed)
        {
            return await RefuseAsync(
                record,
                context,
                subscription,
                meter,
                period,
                allowance,
                correlationId,
                cancellationToken);
        }

        await _thresholds.EvaluateAsync(
            subscription,
            counter,
            correlationId,
            cancellationToken);

        _logger.LogInformation(
            "Usage recorded TenantHash={TenantHash} OrganizationHash={OrganizationHash} " +
            "SubscriptionHash={SubscriptionHash} Meter={Meter} Balance={Balance} " +
            "Included={Included} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(context.OrganizationId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(meter.MeterKey),
            counter.Balance,
            allowance,
            correlationId);

        return SubscriptionOperationResult<UsageResponse>.Success(
            Describe(
                meter,
                period,
                counter.Balance,
                allowance,
                allowed: withinAllowance || meter.OverageAllowed,
                replayed: false),
            correlationId);
    }

    /// <summary>
    /// Undoes a recording that took an enforced meter past its allowance.
    /// </summary>
    /// <remarks>
    /// A compensating entry rather than a deletion: the ledger is append-only, so the refusal
    /// and what caused it both stay visible. The counter is decremented by the same amount, so
    /// a refused call leaves the balance exactly where it was.
    /// </remarks>
    private async Task<SubscriptionOperationResult<UsageResponse>> RefuseAsync(
        SubscriptionUsageRecord record,
        SubscriptionContext context,
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        long allowance,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await _usage.TryAppendRecordAsync(
            new SubscriptionUsageRecord
            {
                TenantId = record.TenantId,
                OrganizationId = record.OrganizationId,
                SubscriptionId = record.SubscriptionId,
                MeterKey = record.MeterKey,
                PeriodKey = record.PeriodKey,
                EntryType = UsageEntryType.Reversal,
                Delta = -record.Delta,
                IdempotencyKey = $"{record.IdempotencyKey}:reversal",
                CompensatesRecordId = record.ItemId,
                OccurredAtUtc = record.OccurredAtUtc,
                CorrelationId = correlationId
            },
            cancellationToken);

        var counter = await _usage.ApplyDeltaAsync(
            SeedFor(context, subscription, meter, period, allowance),
            -record.Delta,
            cancellationToken);

        _logger.LogInformation(
            "Usage refused because the resulting balance is outside its allowed range TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} Meter={Meter} Balance={Balance} " +
            "Included={Included} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(meter.MeterKey),
            counter.Balance,
            allowance,
            correlationId);

        return SubscriptionOperationResult<UsageResponse>.Success(
            Describe(meter, period, counter.Balance, allowance, allowed: false, replayed: false),
            correlationId);
    }

    /// <summary>
    /// Answers a repeated call with the same outcome as the first, without counting it again.
    /// </summary>
    private async Task<SubscriptionOperationResult<UsageResponse>> ReplayAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        long allowance,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var counter = await _usage.GetCounterAsync(
            subscription.TenantId,
            SubscriptionUsageCounter.CreateId(
                subscription.ItemId,
                meter.MeterKey,
                period.Key),
            cancellationToken);

        var balance = counter?.Balance ?? 0;

        return SubscriptionOperationResult<UsageResponse>.Success(
            Describe(
                meter,
                period,
                balance,
                allowance,
                allowed: balance <= allowance || meter.OverageAllowed,
                replayed: true),
            correlationId);
    }

    /// <summary>
    /// How much of a meter this subscription may use before it is over.
    /// </summary>
    /// <remarks>
    /// A trial's grant replaces the plan's allowance rather than adding to it. Where each unit
    /// costs the seller money, a trial that hands out the full monthly quota is an open
    /// invitation to sign up, consume and leave.
    /// </remarks>
    private static long AllowanceFor(SubscriptionDetail subscription, PlanMeter meter)
    {
        if (subscription.Status != SubscriptionStatus.Trialing ||
            subscription.Trial is null)
        {
            return meter.IncludedQuantity;
        }

        var grant = subscription.Trial.Grants.Find(candidate =>
            string.Equals(candidate.MeterKey, meter.MeterKey, StringComparison.Ordinal));

        return grant?.IncludedQuantity ?? meter.IncludedQuantity;
    }

    private SubscriptionUsageCounter SeedFor(
        SubscriptionContext context,
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        long allowance) => new()
    {
        ItemId = SubscriptionUsageCounter.CreateId(
            subscription.ItemId,
            meter.MeterKey,
            period.Key),
        TenantId = context.TenantId,
        OrganizationId = context.OrganizationId,
        SubscriptionId = subscription.ItemId,
        MeterKey = meter.MeterKey,
        PeriodKey = period.Key,
        // Captured when the period opens so a mid-period plan edit cannot re-fire thresholds
        // that have already been reported.
        LimitSnapshot = allowance,
        PeriodStartUtc = period.StartUtc,
        PeriodEndUtc = period.EndUtc,
        ExpiresAtUtc = meter.ResetPolicy == MeterResetPolicy.Never
            ? DateTime.MaxValue
            : period.EndUtc.AddDays(Math.Max(1, _options.CurrentValue.CounterRetentionDays))
    };

    private static PlanMeter? FindMeter(SubscriptionDetail subscription, string meterKey) =>
        subscription.Plan.Meters.Find(meter =>
            string.Equals(meter.MeterKey, meterKey, StringComparison.Ordinal));

    private static UsageResponse Describe(
        PlanMeter meter,
        BillingPeriod period,
        long balance,
        long allowance,
        bool allowed,
        bool replayed) => new()
    {
        Allowed = allowed,
        MeterKey = meter.MeterKey,
        UnitLabel = meter.UnitLabel,
        PeriodKey = period.Key,
        PeriodStartUtc = period.StartUtc,
        PeriodEndUtc = period.EndUtc,
        Included = allowance,
        Used = balance,
        Remaining = Math.Max(0, allowance - balance),
        Overage = Math.Max(0, balance - allowance),
        Replayed = replayed
    };

    private static SubscriptionOperationResult<UsageResponse> Failure(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        SubscriptionOperationResult<UsageResponse>.Failure(
            kind,
            errorCode,
            errorMessage,
            correlationId);
}
