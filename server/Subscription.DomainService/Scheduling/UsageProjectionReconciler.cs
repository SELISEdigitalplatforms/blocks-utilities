using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Brings current-usage projections back in line with the counters they project.
/// </summary>
/// <remarks>
/// Two entry points, because there are two ways a projection falls behind.
/// <para>
/// <b>Targeted.</b> A usage write committed and its synchronous publish did not. The write knows
/// which subscription it was, names it on the queue item, and this republishes every current window
/// for it.
/// </para>
/// <para>
/// <b>Swept.</b> Nothing announced the miss — the process died between the counter update and the
/// scheduling write, or a best-effort announcement was lost. That is the one gap no transaction can
/// close, because the two writes are in different databases. The sweep compares <em>both</em>
/// versions: <c>CounterVersion</c> against the counter's <c>AppliedRecordCount</c>, and
/// <c>SubscriptionVersion</c> against <c>SubscriptionDetail.Version</c>. Comparing only the counter
/// would miss every metadata change, since a plan change or a cancellation moves no counter — and
/// would never reach a cancelled subscription at all, because the backfill walks only the live
/// roster.
/// </para>
/// <para>
/// <b>Backfilled.</b> The document was never written at all — a subscription that predates this
/// collection, a seed that failed, a process that died before the first publish, or a meter added to
/// a plan after the fact. The version sweep cannot find any of those: it reads the projection
/// collection, so a missing document is invisible to it by construction.
/// </para>
/// <para>
/// The API covers that case by falling back to the counters, but <b>a consumer reading this
/// collection directly has nothing to fall back to</b> — it would simply see no meter. So the
/// backfill enumerates the authoritative side instead: it walks the tenant's live subscriptions,
/// resolves the current window of every meter each one defines, and publishes whatever is missing.
/// That is what makes direct access safe to enable, and it is a different question from version lag,
/// which is why it is a separate pass with its own cursor.
/// </para>
/// </remarks>
public interface IUsageProjectionReconciler
{
    Task<int> RefreshSubscriptionAsync(
        string tenantId,
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Repairs projections whose counter has moved on without them.
    /// </summary>
    Task<int> SweepTenantAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Publishes the current window of each named subscription.
    /// </summary>
    /// <remarks>
    /// Called immediately after a period closure with the ids that closure reported having rolled, so
    /// the new window's zero-usage documents exist as soon as the window opens rather than whenever
    /// the backfill next comes round. That is the difference between a direct consumer seeing every meter at one minute
    /// past midnight and seeing only the never-resetting ones.
    /// </remarks>
    Task<int> RefreshManyAsync(
        string tenantId,
        IReadOnlyList<string> subscriptionIds,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Walks the tenant's live subscriptions and publishes any current window that has no document.
    /// </summary>
    /// <remarks>
    /// Bounded per call and resumable: it keeps its own place in the tenant's roster and advances one
    /// page per pass, so a tenant larger than one page is finished by successive passes rather than by
    /// one long pass holding the database. Reaching the end wraps back to the start, because this is a
    /// cycle rather than a migration — a meter added to a plan tomorrow is a missing document
    /// tomorrow.
    /// </remarks>
    Task<UsageProjectionBackfillResult> BackfillTenantAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>Where a backfill pass got to, and what it wrote.</summary>
/// <param name="Examined">Live subscriptions inspected in this pass.</param>
/// <param name="Written">Projected documents created or updated.</param>
/// <param name="LastSubscriptionId">
/// The last subscription seen, which is where the next pass resumes. Null when the roster was
/// exhausted, so the next pass starts again from the beginning.
/// </param>
public sealed record UsageProjectionBackfillResult(
    int Examined,
    int Written,
    string? LastSubscriptionId);

/// <inheritdoc />
public sealed class UsageProjectionReconciler : IUsageProjectionReconciler
{
    private readonly UsageProjectionBackfillCursors _cursors;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionUsageCurrentRepository _current;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly IUsageProjectionPublisher _publisher;
    private readonly UsageProjectionMetrics _metrics;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<UsageProjectionReconciler> _logger;
    private readonly TimeProvider _time;

    public UsageProjectionReconciler(
        ISubscriptionRepository subscriptions,
        ISubscriptionUsageCurrentRepository current,
        ISubscriptionUsageRepository usage,
        IUsageProjectionPublisher publisher,
        // Singleton, so it survives the scope this reconciler lives in. Held as a field here, it was
        // recreated empty on every sweep and the backfill never advanced past its first page.
        UsageProjectionBackfillCursors cursors,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<UsageProjectionReconciler> logger,
        TimeProvider? time = null,
        UsageProjectionMetrics? metrics = null)
    {
        _subscriptions = subscriptions;
        _current = current;
        _usage = usage;
        _publisher = publisher;
        _cursors = cursors;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _metrics = metrics ?? UsageProjectionMetrics.Shared;
    }

    public async Task<int> RefreshSubscriptionAsync(
        string tenantId,
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Re-read rather than trust the queue item, as every handler here must: the item says only
        // that a publish was owed when it was written. The subscription may since have been
        // cancelled or changed plan, and the projection should describe what it is now.
        var subscription = await _subscriptions.GetByIdAsync(
            tenantId,
            subscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            // Gone. Its projections expire on their own TTL, and there is nothing to republish.
            _logger.LogInformation(
                "Skipped a usage projection repair for a subscription that no longer exists " +
                "TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
                "CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(tenantId),
                PaymentLogValue.Hash(subscriptionId),
                correlationId);

            return 0;
        }

        return await _publisher.RefreshAsync(
            subscription,
            _time.GetUtcNow().UtcDateTime,
            correlationId,
            cancellationToken);
    }

    public async Task<int> SweepTenantAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var batch = Math.Max(1, _options.CurrentValue.UsageProjectionReconciliationBatchSize);

        var candidates = await _current.ListBehindCountersAsync(
            tenantId,
            now,
            batch,
            cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        // One batch of counters for the whole candidate set, then compare. Reading the counters is
        // unavoidable here — a version comparison needs both sides — but it need not be one round
        // trip per candidate.
        var counters = await _usage.GetCountersAsync(
            tenantId,
            candidates.Select(candidate => candidate.ItemId).ToList(),
            cancellationToken);

        var behindOnUsage = candidates
            .Where(candidate =>
                counters.TryGetValue(candidate.ItemId, out var counter) &&
                counter.AppliedRecordCount > candidate.CounterVersion)
            .ToList();

        foreach (var candidate in behindOnUsage)
        {
            _metrics.RecordVersionLag(
                counters[candidate.ItemId].AppliedRecordCount - candidate.CounterVersion);
        }

        // Both versions, not just the counter.
        //
        // A counter-only comparison cannot see a metadata change: a plan change, a quantity change or
        // a cancellation moves SubscriptionDetail.Version and leaves the counter exactly where it
        // was. Those changes announce themselves on the queue, but the announcement is best effort by
        // design — it must not fail an operation that has already committed — so if one is lost this
        // is the only thing that finds it.
        //
        // It also reaches subscriptions the backfill does not. The backfill walks the LIVE roster, so
        // a cancelled subscription is outside it forever; its projection is still here, still says
        // whatever it last said, and this is what corrects it to Cancelled.
        var subscriptionIds = candidates
            .Select(candidate => candidate.SubscriptionId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var behind = new List<string>();

        foreach (var subscriptionId in subscriptionIds)
        {
            if (behindOnUsage.Exists(candidate =>
                    string.Equals(candidate.SubscriptionId, subscriptionId, StringComparison.Ordinal)))
            {
                behind.Add(subscriptionId);

                continue;
            }

            // One read per candidate subscription, and only for those whose counters are level. It is
            // affordable because the candidate set is bounded by the batch size and collapsed to
            // distinct subscriptions first, and because a projection whose usage is already current is
            // the common case — this is the only remaining question about it.
            var subscription = await _subscriptions.GetByIdAsync(
                tenantId,
                subscriptionId,
                cancellationToken);

            if (subscription is null)
            {
                continue;
            }

            if (candidates.Any(candidate =>
                    string.Equals(
                        candidate.SubscriptionId, subscriptionId, StringComparison.Ordinal) &&
                    candidate.SubscriptionVersion < subscription.Version))
            {
                behind.Add(subscriptionId);
            }
        }

        var repaired = 0;

        foreach (var subscriptionId in behind)
        {
            repaired += await RefreshSubscriptionAsync(
                tenantId,
                subscriptionId,
                correlationId,
                cancellationToken);
        }

        if (behind.Count > 0)
        {
            _metrics.RecordRepairCompleted("version-lag-sweep", repaired);

            _logger.LogWarning(
                "Repaired usage projections that were behind their counter or their subscription " +
                "TenantHash={TenantHash} Examined={Examined} Subscriptions={Subscriptions} " +
                "Written={Written} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(tenantId),
                candidates.Count,
                behind.Count,
                repaired,
                correlationId);
        }

        return repaired;
    }

    public async Task<int> RefreshManyAsync(
        string tenantId,
        IReadOnlyList<string> subscriptionIds,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriptionIds);

        var written = 0;

        foreach (var subscriptionId in subscriptionIds)
        {
            written += await RefreshSubscriptionAsync(
                tenantId,
                subscriptionId,
                correlationId,
                cancellationToken);
        }

        if (written > 0)
        {
            _metrics.RecordRepairCompleted("period-rollover", written);

            _logger.LogInformation(
                "Published usage projections for windows that just rolled over " +
                "TenantHash={TenantHash} Subscriptions={Subscriptions} Written={Written} " +
                "CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(tenantId),
                subscriptionIds.Count,
                written,
                correlationId);
        }

        return written;
    }

    public async Task<UsageProjectionBackfillResult> BackfillTenantAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var batch = Math.Max(1, _options.CurrentValue.UsageProjectionBackfillBatchSize);

        var afterSubscriptionId = _cursors.Resume(tenantId);

        var subscriptions = await _subscriptions.ListLivePageAsync(
            tenantId,
            afterSubscriptionId,
            batch,
            cancellationToken);

        if (subscriptions.Count == 0)
        {
            // The roster is exhausted, so the next pass starts from the beginning. That makes this a
            // cycle rather than a one-off migration, which it has to be: a meter added to a plan
            // tomorrow is a missing document tomorrow.
            _cursors.Advance(tenantId, null);

            return new UsageProjectionBackfillResult(0, 0, null);
        }

        var written = 0;

        foreach (var subscription in subscriptions)
        {
            // RefreshAsync is what publishes; this pass only decides who needs asking. It seeds a
            // window with no counter and publishes one that has, both conditionally, so a backfill
            // running beside live recordings cannot overwrite anything newer than what it read.
            written += await _publisher.RefreshAsync(
                subscription,
                now,
                correlationId,
                cancellationToken);
        }

        if (written > 0)
        {
            _metrics.RecordRepairCompleted("backfill", written);

            _logger.LogInformation(
                "Usage projection backfill wrote missing documents TenantHash={TenantHash} " +
                "Examined={Examined} Written={Written} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(tenantId),
                subscriptions.Count,
                written,
                correlationId);
        }

        // Only a full page can have more behind it. A short page means the roster ended here, so the
        // next pass restarts rather than asking for rows after the last id forever.
        var resumeFrom = subscriptions.Count == batch ? subscriptions[^1].ItemId : null;

        _cursors.Advance(tenantId, resumeFrom);

        return new UsageProjectionBackfillResult(subscriptions.Count, written, resumeFrom);
    }
}

/// <summary>
/// Republishes a subscription's current-usage projection, or sweeps a tenant's for version lag.
/// </summary>
/// <remarks>
/// Both shapes, following the convention the financial-document and renewal handlers already set:
/// an item scheduled where the miss happened names the subscription, and one scheduled by the repair
/// sweep names nothing because its job is to find what nobody announced.
/// <para>
/// Nothing here can charge anybody, and it holds no processor that could. It writes to one
/// collection, which no billing decision reads.
/// </para>
/// </remarks>
public sealed class UsageProjectionRefreshWorkHandler : ISubscriptionWorkHandler
{
    private readonly IUsageProjectionReconciler _reconciler;

    public UsageProjectionRefreshWorkHandler(IUsageProjectionReconciler reconciler) =>
        _reconciler = reconciler;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.UsageProjectionRefresh;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var correlationId = string.IsNullOrWhiteSpace(work.CorrelationId)
            ? work.ItemId
            : work.CorrelationId;

        if (!string.IsNullOrWhiteSpace(work.AggregateId))
        {
            await _reconciler.RefreshSubscriptionAsync(
                work.TenantId,
                work.AggregateId,
                correlationId,
                cancellationToken);

            return SubscriptionWorkOutcome.Completed();
        }

        // Tenant-wide: both passes, because they answer different questions. The sweep finds
        // documents whose counter moved on without them; the backfill finds windows that have no
        // document at all, which the sweep cannot see because it reads the projection collection.
        await _reconciler.SweepTenantAsync(work.TenantId, correlationId, cancellationToken);
        await _reconciler.BackfillTenantAsync(work.TenantId, correlationId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}
