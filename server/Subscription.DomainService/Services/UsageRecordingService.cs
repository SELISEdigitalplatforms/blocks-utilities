using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
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
    private readonly IUsagePeriodClosureRepository _closures;
    private readonly IMeterAllowanceResolver _allowances;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IUsageThresholdEvaluator _thresholds;
    private readonly IUsageProjectionPublisher _projection;
    private readonly ISubscriptionUsageCurrentRepository _current;
    private readonly ISubscriptionWorkScheduler _scheduler;
    private readonly UsageProjectionMetrics _metrics;
    private readonly IValidator<RecordUsageRequest> _validator;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<UsageRecordingService> _logger;
    private readonly TimeProvider _time;

    public UsageRecordingService(
        ISubscriptionRepository subscriptions,
        ISubscriptionUsageRepository usage,
        IUsagePeriodClosureRepository closures,
        IMeterAllowanceResolver allowances,
        ISubscriptionContextResolver contextResolver,
        IUsageThresholdEvaluator thresholds,
        IUsageProjectionPublisher projection,
        ISubscriptionUsageCurrentRepository current,
        ISubscriptionWorkScheduler scheduler,
        IValidator<RecordUsageRequest> validator,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<UsageRecordingService> logger,
        TimeProvider? time = null,
        // Optional so an existing caller or test that builds this service by hand keeps compiling.
        // Falls back to the shared instrument set, which is what the running process uses anyway.
        UsageProjectionMetrics? metrics = null)
    {
        _subscriptions = subscriptions;
        _usage = usage;
        _closures = closures;
        _allowances = allowances;
        _contextResolver = contextResolver;
        _thresholds = thresholds;
        _projection = projection;
        _current = current;
        _scheduler = scheduler;
        _metrics = metrics ?? UsageProjectionMetrics.Shared;
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
        var readAt = _time.GetUtcNow().UtcDateTime;

        var subscription = await _subscriptions.GetLiveAsync(
            context.TenantId,
            context.OrganizationId,
            readAt,
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

        // Against the meter this subscription was sold, never the catalogue's current terms — the
        // snapshot is what its allowance and its rating are measured by, so it has to be what its
        // granularity is measured by too. A meter that never declared a scale reads zero and
        // refuses any fraction, which is how every meter behaved before fractions existed.
        if (!MeterQuantity.IsWithinScale(request.Quantity, meter.QuantityScale) ||
            !MeterQuantity.IsWithinMagnitude(request.Quantity))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_usage_quantity_scale_invalid",
                meter.QuantityScale == 0
                    ? "This meter takes whole numbers only."
                    : $"This meter allows at most {meter.QuantityScale} decimal places.",
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
        var read = await ReadCurrentAsync(
            organizationId,
            UsageReadMode.Authoritative,
            correlationId,
            cancellationToken);

        return read.IsSuccess
            ? SubscriptionOperationResult<IReadOnlyList<UsageResponse>>.Success(
                read.Value!.Items,
                correlationId)
            : SubscriptionOperationResult<IReadOnlyList<UsageResponse>>.Failure(
                read.FailureKind,
                read.ErrorCode!,
                read.ErrorMessage!,
                correlationId);
    }

    public async Task<SubscriptionOperationResult<UsageCurrentRead>> ReadCurrentAsync(
        string? organizationId,
        UsageReadMode readMode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var startedAt = _time.GetTimestamp();

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            organizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<UsageCurrentRead>(correlationId);
        }

        var context = resolution.Context!;
        var now = _time.GetUtcNow().UtcDateTime;

        var subscription = await _subscriptions.GetLiveAsync(
            context.TenantId,
            context.OrganizationId,
            now,
            cancellationToken);

        if (subscription is null)
        {
            return SubscriptionOperationResult<UsageCurrentRead>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "This organization has no active subscription.",
                correlationId);
        }

        var fallback = UsageReadFallback.None;

        if (readMode == UsageReadMode.Projection)
        {
            var projected = await ReadProjectionAsync(context, subscription, now, cancellationToken);

            // How many meter-windows the plan says this subscription has right now. The projection is
            // only allowed to answer if it holds all of them.
            var expected = CountCurrentWindows(subscription, now);

            if (projected.Count == expected && expected > 0)
            {
                return ReadOf(
                    projected,
                    readMode,
                    UsageReadMode.Projection,
                    UsageReadFallback.None,
                    startedAt,
                    now,
                    context,
                    subscription,
                    correlationId);
            }

            // Two different situations, reported as two different fallbacks.
            //
            // Nothing published is a subscription the projection has never covered - activated before
            // the collection existed, or a seed that has not run. Some published is worse: a publish
            // or a seed was lost for a subscription it does cover.
            //
            // Either way the counters answer the WHOLE request, not just the missing part. Returning
            // the published subset would omit meters the plan defines, with nothing in the body to
            // say so, and a caller drawing a usage screen from it would show fewer meters than the
            // subscription has. The two modes must return equivalent data, and a subset is not
            // equivalent.
            fallback = projected.Count == 0
                ? UsageReadFallback.ProjectionEmpty
                : UsageReadFallback.ProjectionPartial;

            // A partial projection is a lost write, so it is repaired rather than merely reported.
            // Best effort: the read has an answer either way, and it must not fail because a repair
            // could not be scheduled.
            if (fallback == UsageReadFallback.ProjectionPartial)
            {
                _logger.LogWarning(
                    "Usage projection is incomplete for this subscription; falling back to counters " +
                    "and scheduling a repair TenantHash={TenantHash} " +
                    "OrganizationHash={OrganizationHash} SubscriptionHash={SubscriptionHash} " +
                    "Published={Published} Expected={Expected} CorrelationId={CorrelationId}",
                    PaymentLogValue.Hash(context.TenantId),
                    PaymentLogValue.Hash(context.OrganizationId),
                    PaymentLogValue.Hash(subscription.ItemId),
                    projected.Count,
                    expected,
                    correlationId);

                await _scheduler.ScheduleUsageProjectionRefreshAsync(
                    context.TenantId,
                    context.OrganizationId,
                    subscription.ItemId,
                    correlationId,
                    cancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    "Usage projection holds nothing for this subscription; falling back to counters " +
                    "TenantHash={TenantHash} OrganizationHash={OrganizationHash} " +
                    "SubscriptionHash={SubscriptionHash} CorrelationId={CorrelationId}",
                    PaymentLogValue.Hash(context.TenantId),
                    PaymentLogValue.Hash(context.OrganizationId),
                    PaymentLogValue.Hash(subscription.ItemId),
                    correlationId);
            }
        }

        var authoritative = await ReadAuthoritativeAsync(
            context,
            subscription,
            now,
            correlationId,
            cancellationToken);

        return authoritative.IsSuccess
            ? ReadOf(
                authoritative.Value!,
                readMode,
                UsageReadMode.Authoritative,
                fallback,
                startedAt,
                newestAgeSeconds: null,
                stale: false,
                context,
                subscription,
                correlationId)
            : SubscriptionOperationResult<UsageCurrentRead>.Failure(
                authoritative.FailureKind,
                authoritative.ErrorCode!,
                authoritative.ErrorMessage!,
                correlationId);
    }

    /// <summary>
    /// How many meter-windows the plan says are current, which is what a complete projection holds.
    /// </summary>
    /// <remarks>
    /// A meter whose period cannot be resolved is not counted. Its absence from the projection is
    /// then not treated as incompleteness, which matters because the authoritative read refuses the
    /// whole request for such a meter — counting it here would make every projection read of an
    /// unresolvable subscription report a partial fallback on the way to that refusal.
    /// </remarks>
    private static int CountCurrentWindows(SubscriptionDetail subscription, DateTime asOfUtc)
    {
        var windows = 0;

        foreach (var meter in subscription.Plan.Meters)
        {
            if (MeterPeriodResolver.TryGetPeriod(subscription, meter, asOfUtc, out _))
            {
                windows++;
            }
        }

        return windows;
    }

    /// <summary>
    /// Current usage from the authoritative counters, in one round trip rather than one per meter.
    /// </summary>
    /// <remarks>
    /// Batched by composed counter id, not by period key. The meters of one subscription do not share
    /// a period: a never-reset capacity meter lives under
    /// <c>MeterPeriodResolver.LifetimePeriodKey</c> while its periodic neighbours use the billing
    /// schedule's key. A batch filtered by any single period would quietly have returned nothing for
    /// the others and reported them as unused.
    /// </remarks>
    private async Task<SubscriptionOperationResult<List<UsageResponse>>> ReadAuthoritativeAsync(
        SubscriptionContext context,
        SubscriptionDetail subscription,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var windows = new List<(PlanMeter Meter, BillingPeriod Period)>();

        foreach (var meter in subscription.Plan.Meters)
        {
            if (!MeterPeriodResolver.TryGetPeriod(subscription, meter, now, out var period))
            {
                return SubscriptionOperationResult<List<UsageResponse>>.Failure(
                    PaymentFailureKind.Unavailable,
                    "subscription_schedule_unavailable",
                    "The usage period could not be determined.",
                    correlationId);
            }

            windows.Add((meter, period));
        }

        var counters = await _usage.GetCountersAsync(
            context.TenantId,
            windows
                .Select(window => SubscriptionUsageCounter.CreateId(
                    subscription.ItemId,
                    window.Meter.MeterKey,
                    window.Period.Key))
                .ToList(),
            cancellationToken);

        var responses = new List<UsageResponse>(windows.Count);

        foreach (var (meter, period) in windows)
        {
            counters.TryGetValue(
                SubscriptionUsageCounter.CreateId(subscription.ItemId, meter.MeterKey, period.Key),
                out var counter);

            responses.Add(Describe(
                meter,
                period,
                counter?.Balance ?? 0,
                await _allowances.EffectiveAsync(
                    subscription, meter, period, counter, cancellationToken),
                allowed: true,
                replayed: false));
        }

        return SubscriptionOperationResult<List<UsageResponse>>.Success(responses, correlationId);
    }

    /// <summary>
    /// Current usage from the projection, in one indexed query for the whole subscription.
    /// </summary>
    /// <remarks>
    /// Returns only meters the plan still defines. A projected document outlives a plan change until
    /// its refresh runs, and showing a reader an allowance for a meter the current plan no longer has
    /// would be an allowance nothing can be recorded against.
    /// </remarks>
    private async Task<List<(UsageResponse Response, DateTime UpdatedAtUtc)>> ReadProjectionAsync(
        SubscriptionContext context,
        SubscriptionDetail subscription,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var documents = await _current.ListCurrentAsync(
            context.TenantId,
            context.OrganizationId,
            subscription.ItemId,
            now,
            cancellationToken);

        var meters = new HashSet<string>(
            subscription.Plan.Meters.Select(meter => meter.MeterKey),
            StringComparer.Ordinal);

        var results = new List<(UsageResponse, DateTime)>(documents.Count);

        foreach (var document in documents)
        {
            if (!meters.Contains(document.MeterKey))
            {
                continue;
            }

            results.Add((
                new UsageResponse
                {
                    // True because this is a report, not a claim. Whether the next unit may be used
                    // is answered by POST with enforce, and only there.
                    Allowed = true,
                    MeterKey = document.MeterKey,
                    UnitLabel = document.UnitLabel,
                    PeriodKey = document.PeriodKey,
                    PeriodStartUtc = document.PeriodStartUtc,
                    PeriodEndUtc = document.PeriodEndUtc,
                    Included = document.Included,
                    Used = document.Used,
                    Remaining = document.Remaining,
                    Overage = document.Overage,
                    Replayed = false
                },
                document.UpdatedAtUtc));
        }

        return results;
    }

    private SubscriptionOperationResult<UsageCurrentRead> ReadOf(
        List<(UsageResponse Response, DateTime UpdatedAtUtc)> projected,
        UsageReadMode requested,
        UsageReadMode actual,
        UsageReadFallback fallback,
        long startedAt,
        DateTime now,
        SubscriptionContext context,
        SubscriptionDetail subscription,
        string correlationId)
    {
        var threshold = TimeSpan.FromSeconds(
            Math.Max(1, _options.CurrentValue.UsageProjectionStalenessSeconds));

        var newest = projected.Max(entry => entry.UpdatedAtUtc);

        return ReadOf(
            projected.ConvertAll(entry => entry.Response),
            requested,
            actual,
            fallback,
            startedAt,
            (now - newest).TotalSeconds,
            projected.Exists(entry => now - entry.UpdatedAtUtc > threshold),
            context,
            subscription,
            correlationId);
    }

    private SubscriptionOperationResult<UsageCurrentRead> ReadOf(
        List<UsageResponse> items,
        UsageReadMode requested,
        UsageReadMode actual,
        UsageReadFallback fallback,
        long startedAt,
        double? newestAgeSeconds,
        bool stale,
        SubscriptionContext context,
        SubscriptionDetail subscription,
        string correlationId)
    {
        var duration = _time.GetElapsedTime(startedAt);

        var diagnostics = new UsageReadDiagnostics
        {
            RequestedMode = requested,
            ActualMode = actual,
            Fallback = fallback,
            DurationMs = duration.TotalMilliseconds,
            DocumentCount = items.Count,
            NewestProjectionAgeSeconds = newestAgeSeconds,
            Stale = stale
        };

        _metrics.RecordRead(requested, actual, fallback, duration, newestAgeSeconds, stale);

        // Slow, stale and fallen-back reads are always logged; an ordinary read is sampled down to
        // debug, because this is one line per call of a dashboard endpoint.
        //
        // Every line names the tenant, organization and subscription. Without them "a read was slow"
        // cannot be turned into "which customer saw it", which is the first question anybody asks.
        // Hashed rather than raw, following PaymentLogValue everywhere else in this module: enough to
        // group and correlate, not enough to identify a subscriber from the log alone. These belong
        // in logs and traces and NOT in metric labels, where a per-tenant dimension would multiply
        // every series by the tenant count.
        if (stale ||
            fallback != UsageReadFallback.None ||
            duration.TotalMilliseconds >= _options.CurrentValue.UsageReadSlowMilliseconds)
        {
            _logger.LogWarning(
                "Current usage read was slow, stale or fell back Mode={Mode} " +
                "ActualMode={ActualMode} Fallback={Fallback} DurationMs={DurationMs} " +
                "Documents={Documents} NewestProjectionAgeSeconds={NewestProjectionAgeSeconds} " +
                "Stale={Stale} TenantHash={TenantHash} OrganizationHash={OrganizationHash} " +
                "SubscriptionHash={SubscriptionHash} CorrelationId={CorrelationId} " +
                "TraceId={TraceId}",
                requested,
                actual,
                fallback,
                duration.TotalMilliseconds,
                items.Count,
                newestAgeSeconds,
                stale,
                PaymentLogValue.Hash(context.TenantId),
                PaymentLogValue.Hash(context.OrganizationId),
                PaymentLogValue.Hash(subscription.ItemId),
                correlationId,
                System.Diagnostics.Activity.Current?.TraceId.ToString());
        }
        else
        {
            _logger.LogDebug(
                "Current usage read Mode={Mode} ActualMode={ActualMode} DurationMs={DurationMs} " +
                "Documents={Documents} TenantHash={TenantHash} " +
                "OrganizationHash={OrganizationHash} SubscriptionHash={SubscriptionHash} " +
                "CorrelationId={CorrelationId} TraceId={TraceId}",
                requested,
                actual,
                duration.TotalMilliseconds,
                items.Count,
                PaymentLogValue.Hash(context.TenantId),
                PaymentLogValue.Hash(context.OrganizationId),
                PaymentLogValue.Hash(subscription.ItemId),
                correlationId,
                System.Diagnostics.Activity.Current?.TraceId.ToString());
        }

        // The same identifiers on the trace span, so a slow read can be found from a trace rather
        // than only by grepping logs for a correlation id.
        var span = System.Diagnostics.Activity.Current;

        if (span is not null)
        {
            span.SetTag("subscription.usage.read_mode", requested.ToString());
            span.SetTag("subscription.usage.read_source", actual.ToString());
            span.SetTag("subscription.usage.fallback", fallback.ToString());
            span.SetTag("subscription.usage.duration_ms", duration.TotalMilliseconds);
            span.SetTag("subscription.usage.documents", items.Count);
            span.SetTag("subscription.usage.stale", stale);
            span.SetTag("subscription.tenant_hash", PaymentLogValue.Hash(context.TenantId));
            span.SetTag(
                "subscription.organization_hash",
                PaymentLogValue.Hash(context.OrganizationId));
            span.SetTag("subscription.subscription_hash", PaymentLogValue.Hash(subscription.ItemId));
            span.SetTag("subscription.correlation_id", correlationId);
        }

        return SubscriptionOperationResult<UsageCurrentRead>.Success(
            new UsageCurrentRead { Items = items, Diagnostics = diagnostics },
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
        // A hold against this exact period, taken out atomically against its closure state —
        // Open, and before its boundary if one is set. Nothing about the subscription document
        // itself is re-read here: a period stops admitting claims because SOMETHING marked its
        // closure record Closing (SubscriptionCancellationService.EndNowAsync, or
        // SubscriptionCancellationEffectiveProcessor.TryFinalizeAsync), independent of whichever
        // "now" this request's own earlier GetLiveAsync call happened to read. Rating waits for
        // every outstanding claim to release before it prices this same period — see
        // SubscriptionUsageRatingProcessor — so a claim held here is what makes it impossible for
        // an invoice to be generated while this call could still change the balance it prices.
        var claim = await _closures.TryAcquireClaimAsync(
            context.TenantId,
            subscription.ItemId,
            period.Key,
            request.IdempotencyKey,
            occurredAt,
            cancellationToken);

        if (claim == UsageClaimOutcome.Rejected)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "This organization has no active subscription.",
                correlationId);
        }

        try
        {
            var opening = await _allowances.OpeningAllowanceAsync(
                subscription,
                meter,
                period,
                cancellationToken);

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
                    opening,
                    correlationId,
                    cancellationToken);
            }

            var counter = await _usage.ApplyDeltaAsync(
                SeedFor(context, subscription, meter, period, opening),
                request.Quantity,
                cancellationToken);

            // The window's own snapshot, not the figure just computed: it was frozen when the
            // window opened, so a carried-forward allowance cannot shift mid-window because the
            // previous window's counter was repaired or the plan was edited underneath this
            // caller.
            var allowance = MeterAllowance.Effective(counter, opening);

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

            // Published from the final counter state, synchronously, before this call reports
            // success. Placed after every branch that could still reverse the balance, so what a
            // direct reader sees is the figure this response carries and never the momentary one an
            // enforced refusal passes through on its way to being undone.
            var projection = await _projection.PublishAsync(
                subscription,
                meter,
                period,
                counter,
                allowance,
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
                    replayed: false,
                    projection),
                correlationId);
        }
        finally
        {
            // Released only by whichever call actually acquired the claim fresh. A concurrent
            // duplicate that saw AlreadyClaimed must not release it here: the call that is still
            // holding it — genuinely in flight, mid-write — has not finished yet, and releasing
            // on its behalf would let rating proceed while that write is still outstanding. A
            // sequential retry after the original call already completed and released needs no
            // release of its own either, since there is nothing left to reverse.
            if (claim == UsageClaimOutcome.Acquired)
            {
                await _closures.ReleaseClaimAsync(
                    context.TenantId,
                    subscription.ItemId,
                    period.Key,
                    request.IdempotencyKey,
                    cancellationToken);
            }
        }
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
        decimal allowance,
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

        // The post-reversal counter, which is the balance the caller is about to be shown and the
        // only one a reader should ever see. The exceeded balance existed for the duration of two
        // Mongo calls and was never a state anybody was entitled to.
        var projection = await _projection.PublishAsync(
            subscription,
            meter,
            period,
            counter,
            allowance,
            correlationId,
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
            Describe(
                meter,
                period,
                counter.Balance,
                allowance,
                allowed: false,
                replayed: false,
                projection),
            correlationId);
    }

    /// <summary>
    /// Answers a repeated call with the same outcome as the first, without counting it again.
    /// </summary>
    private async Task<SubscriptionOperationResult<UsageResponse>> ReplayAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        decimal allowance,
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

        // A replay counts nothing again, but it is also the retry a caller was told to make when a
        // publish failed — so it republishes. The version condition makes that free when the
        // projection is already current: the write matches nothing and changes nothing.
        var projection = UsageProjectionState.Published;

        if (counter is not null)
        {
            projection = ToState(await _projection.PublishAsync(
                subscription,
                meter,
                period,
                counter,
                allowance,
                correlationId,
                cancellationToken));
        }

        return SubscriptionOperationResult<UsageResponse>.Success(
            Describe(
                meter,
                period,
                balance,
                allowance,
                allowed: balance <= allowance || meter.OverageAllowed,
                replayed: true,
                projection),
            correlationId);
    }

    private SubscriptionUsageCounter SeedFor(
        SubscriptionContext context,
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        decimal allowance) => new()
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
        decimal balance,
        decimal allowance,
        bool allowed,
        bool replayed,
        UsageProjectionOutcome projection) =>
        Describe(meter, period, balance, allowance, allowed, replayed, ToState(projection));

    private static UsageResponse Describe(
        PlanMeter meter,
        BillingPeriod period,
        decimal balance,
        decimal allowance,
        bool allowed,
        bool replayed,
        UsageProjectionState projection = UsageProjectionState.Published) => new()
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
        Replayed = replayed,
        Projection = projection
    };

    /// <summary>
    /// Superseded is reported as published, deliberately: it means a later recording against this
    /// meter published a newer figure first, so the projection is ahead of this caller rather than
    /// missing. Only a write that did not happen is pending.
    /// </summary>
    private static UsageProjectionState ToState(UsageProjectionOutcome outcome) =>
        outcome == UsageProjectionOutcome.RepairScheduled
            ? UsageProjectionState.Pending
            : UsageProjectionState.Published;

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
