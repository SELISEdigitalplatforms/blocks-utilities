using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <inheritdoc cref="IUsageProjectionPublisher"/>
public sealed class UsageProjectionPublisher : IUsageProjectionPublisher
{
    private readonly ISubscriptionUsageCurrentRepository _current;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly IMeterAllowanceResolver _allowances;
    private readonly ISubscriptionWorkScheduler _scheduler;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<UsageProjectionPublisher> _logger;
    private readonly TimeProvider _time;

    public UsageProjectionPublisher(
        ISubscriptionUsageCurrentRepository current,
        ISubscriptionUsageRepository usage,
        IMeterAllowanceResolver allowances,
        ISubscriptionWorkScheduler scheduler,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<UsageProjectionPublisher> logger,
        TimeProvider? time = null)
    {
        _current = current;
        _usage = usage;
        _allowances = allowances;
        _scheduler = scheduler;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<UsageProjectionOutcome> PublishAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        SubscriptionUsageCounter counter,
        long allowance,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(counter);

        var document = Describe(subscription, meter, period, counter, allowance);

        var started = _time.GetTimestamp();

        try
        {
            var published = await WithTransientRetryAsync(
                () => _current.TryPublishAsync(document, cancellationToken),
                cancellationToken);

            LogPublished(subscription, meter, document, started, published, correlationId);

            return published
                ? UsageProjectionOutcome.Published
                : UsageProjectionOutcome.Superseded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The usage is already committed. Swallowing it here and scheduling a repair is the
            // whole point: this is a read model, and letting it throw would turn a display problem
            // into a failed billing write.
            _logger.LogError(
                exception,
                "Usage projection publication failed after the usage committed; scheduling a repair " +
                "TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} Meter={Meter} " +
                "Period={Period} SourceVersion={SourceVersion} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(meter.MeterKey),
                PaymentLogValue.Label(period.Key),
                document.SourceVersion,
                correlationId);

            await ScheduleRepairAsync(subscription, correlationId, cancellationToken);

            return UsageProjectionOutcome.RepairScheduled;
        }
    }

    public async Task<int> SeedCurrentAsync(
        SubscriptionDetail subscription,
        DateTime asOfUtc,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var seeded = 0;

        foreach (var (meter, period) in CurrentWindows(subscription, asOfUtc))
        {
            // The opening allowance rather than the effective one: there is no counter yet, so there
            // is nothing whose snapshot could differ from it.
            var allowance = await _allowances.OpeningAllowanceAsync(
                subscription,
                meter,
                period,
                cancellationToken);

            var document = Describe(
                subscription,
                meter,
                period,
                counter: null,
                balance: 0,
                sourceVersion: 0,
                allowance);

            try
            {
                if (await WithTransientRetryAsync(
                        () => _current.TrySeedAsync(document, cancellationToken),
                        cancellationToken))
                {
                    seeded++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A missing zero-usage document is a discovery gap, not lost usage: the first
                // recording publishes the meter anyway. Scheduled for repair and not propagated,
                // because the caller of this is an activation or a rollover that has already
                // committed something more important.
                _logger.LogWarning(
                    exception,
                    "Could not seed a zero-usage projection; scheduling a repair " +
                    "TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} Meter={Meter} " +
                    "CorrelationId={CorrelationId}",
                    PaymentLogValue.Hash(subscription.TenantId),
                    PaymentLogValue.Hash(subscription.ItemId),
                    PaymentLogValue.Label(meter.MeterKey),
                    correlationId);

                await ScheduleRepairAsync(subscription, correlationId, cancellationToken);
            }
        }

        return seeded;
    }

    public async Task<int> RefreshAsync(
        SubscriptionDetail subscription,
        DateTime asOfUtc,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var windows = CurrentWindows(subscription, asOfUtc).ToList();

        if (windows.Count == 0)
        {
            return 0;
        }

        // One batch for every meter, which matters here as much as on the read path: a repair over a
        // subscription with a dozen meters would otherwise be a dozen round trips.
        var counters = await _usage.GetCountersAsync(
            subscription.TenantId,
            windows
                .Select(window => SubscriptionUsageCounter.CreateId(
                    subscription.ItemId,
                    window.Meter.MeterKey,
                    window.Period.Key))
                .ToList(),
            cancellationToken);

        var published = 0;

        foreach (var (meter, period) in windows)
        {
            counters.TryGetValue(
                SubscriptionUsageCounter.CreateId(subscription.ItemId, meter.MeterKey, period.Key),
                out var counter);

            var allowance = await _allowances.EffectiveAsync(
                subscription,
                meter,
                period,
                counter,
                cancellationToken);

            SubscriptionUsageCurrent document;

            if (counter is null)
            {
                // No counter means nothing has been recorded in this window. Seeded rather than
                // published: a publish carries version 0, which the version condition would refuse
                // against any existing document, and would be wrong to accept if it did.
                document = Describe(
                    subscription, meter, period, counter: null, balance: 0, sourceVersion: 0, allowance);

                if (await _current.TrySeedAsync(document, cancellationToken))
                {
                    published++;
                }

                continue;
            }

            document = Describe(subscription, meter, period, counter, allowance);

            if (await _current.TryPublishAsync(document, cancellationToken))
            {
                published++;
            }
        }

        _logger.LogInformation(
            "Usage projection refreshed TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "Windows={Windows} Written={Written} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            windows.Count,
            published,
            correlationId);

        return published;
    }

    /// <summary>
    /// Every meter's window containing <paramref name="asOfUtc"/>.
    /// </summary>
    /// <remarks>
    /// A meter whose period cannot be resolved is skipped rather than failing the whole refresh. That
    /// happens when a schedule is unavailable, which is a condition the authoritative endpoint reports
    /// as an error in its own right; dropping the projection for every other meter as well would turn
    /// one unresolvable meter into a subscription with no visible usage at all.
    /// </remarks>
    private static IEnumerable<(PlanMeter Meter, BillingPeriod Period)> CurrentWindows(
        SubscriptionDetail subscription,
        DateTime asOfUtc)
    {
        foreach (var meter in subscription.Plan.Meters)
        {
            if (MeterPeriodResolver.TryGetPeriod(subscription, meter, asOfUtc, out var period))
            {
                yield return (meter, period);
            }
        }
    }

    private SubscriptionUsageCurrent Describe(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        SubscriptionUsageCounter counter,
        long allowance) =>
        Describe(
            subscription,
            meter,
            period,
            counter,
            counter.Balance,
            counter.AppliedRecordCount,
            allowance);

    private SubscriptionUsageCurrent Describe(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        SubscriptionUsageCounter? counter,
        long balance,
        long sourceVersion,
        long allowance) => new()
    {
        ItemId = SubscriptionUsageCurrent.CreateId(
            subscription.ItemId,
            meter.MeterKey,
            period.Key),
        TenantId = subscription.TenantId,
        OrganizationId = subscription.OrganizationId,
        SubscriptionId = subscription.ItemId,
        SubscriptionStatus = subscription.Status,
        PlanId = subscription.Plan.PlanId,
        PlanCode = subscription.Plan.Code,
        MeterKey = meter.MeterKey,
        UnitLabel = meter.UnitLabel,
        PeriodKey = period.Key,
        PeriodStartUtc = period.StartUtc,
        PeriodEndUtc = period.EndUtc,
        Included = allowance,
        Used = balance,
        // The same arithmetic the authoritative response reports, in one place, so the two cannot
        // describe the same balance differently. Derived from the figures above and never from a
        // previous value of this document.
        Remaining = Math.Max(0, allowance - balance),
        Overage = Math.Max(0, balance - allowance),
        OverageAllowed = meter.OverageAllowed,
        SourceVersion = sourceVersion,
        SchemaVersion = SubscriptionUsageCurrent.CurrentSchemaVersion,
        UpdatedAtUtc = _time.GetUtcNow().UtcDateTime,
        // The counter's own expiry when there is one, so the projection never outlives what it
        // projects. Without a counter the window's end plus the same retention, which is what the
        // counter would have been given.
        ExpiresAtUtc = counter?.ExpiresAtUtc ?? (
            meter.ResetPolicy == MeterResetPolicy.Never
                ? DateTime.MaxValue
                : period.EndUtc.AddDays(Math.Max(1, _options.CurrentValue.CounterRetentionDays)))
    };

    /// <summary>
    /// One line per publish, at debug unless it took long enough to matter.
    /// </summary>
    /// <remarks>
    /// Sampled down deliberately: this runs on every metered usage call, so logging each one at
    /// information would make the projection the loudest thing in the log and bury the failures that
    /// matter. Slow publishes are always logged, because a slow publish is latency added to a
    /// customer-facing billing call.
    /// </remarks>
    private void LogPublished(
        SubscriptionDetail subscription,
        PlanMeter meter,
        SubscriptionUsageCurrent document,
        long startedAt,
        bool written,
        string correlationId)
    {
        var duration = _time.GetElapsedTime(startedAt);

        if (duration.TotalMilliseconds >= _options.CurrentValue.UsageReadSlowMilliseconds)
        {
            _logger.LogWarning(
                "Usage projection publish was slow TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Meter={Meter} DurationMs={DurationMs} " +
                "Written={Written} SourceVersion={SourceVersion} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(meter.MeterKey),
                duration.TotalMilliseconds,
                written,
                document.SourceVersion,
                correlationId);

            return;
        }

        _logger.LogDebug(
            "Usage projection published TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "Meter={Meter} DurationMs={DurationMs} Written={Written} " +
            "SourceVersion={SourceVersion} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(meter.MeterKey),
            duration.TotalMilliseconds,
            written,
            document.SourceVersion,
            correlationId);
    }

    private Task ScheduleRepairAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken) =>
        _scheduler.ScheduleUsageProjectionRefreshAsync(
            subscription.TenantId,
            subscription.OrganizationId,
            subscription.ItemId,
            correlationId,
            cancellationToken);

    /// <summary>
    /// Retries a projection write a couple of times for the errors that are worth retrying.
    /// </summary>
    /// <remarks>
    /// Brief and bounded, because this runs inside a request that has already committed its usage:
    /// the caller is waiting, and a long retry would make a slow projection look like a slow billing
    /// API. Two extra attempts with a short pause covers a primary stepping down or a connection
    /// dropping; anything longer-lived is the repair job's problem, which is not holding a request
    /// open.
    /// <para>
    /// Only transient errors. A duplicate-key or a serialization fault would fail identically on
    /// every attempt, so retrying it just spends the caller's time before reaching the same repair.
    /// </para>
    /// </remarks>
    private async Task<bool> WithTransientRetryAsync(
        Func<Task<bool>> write,
        CancellationToken cancellationToken)
    {
        const int attempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await write();
            }
            catch (Exception exception)
                when (attempt < attempts && IsTransient(exception) &&
                      !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        MongoConnectionException => true,
        MongoNotPrimaryException => true,
        MongoNodeIsRecoveringException => true,
        MongoExecutionTimeoutException => true,
        TimeoutException => true,
        MongoCommandException command => command.Code is 11600 or 11602 or 189 or 91,
        _ => false
    };
}
