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
/// scheduling write, which is the one gap that cannot be closed without a transaction across two
/// databases. The sweep finds projections whose <c>SourceVersion</c> is behind their counter's
/// <c>AppliedRecordCount</c> and republishes them.
/// </para>
/// <para>
/// <b>What the sweep does not find.</b> A projection that was never written at all. It reads the
/// projection collection, so a missing document is invisible to it by construction. Three other
/// things cover that case, and they are why finding it here would be redundant rather than merely
/// hard: activation and period rollover seed a zero-usage document for every meter; the first usage
/// recording publishes one whether or not a seed exists; and a projection read that finds nothing
/// falls back to the counters instead of reporting an empty allowance. Discovering it here would mean
/// scanning the counters of every live subscription per tenant, which is a new index and a new
/// per-tenant cost on the hot billing collection, to repair something already covered on three
/// paths.
/// </para>
/// </remarks>
public interface IUsageProjectionReconciler
{
    Task<int> RefreshSubscriptionAsync(
        string tenantId,
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<int> SweepTenantAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class UsageProjectionReconciler : IUsageProjectionReconciler
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionUsageCurrentRepository _current;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly IUsageProjectionPublisher _publisher;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<UsageProjectionReconciler> _logger;
    private readonly TimeProvider _time;

    public UsageProjectionReconciler(
        ISubscriptionRepository subscriptions,
        ISubscriptionUsageCurrentRepository current,
        ISubscriptionUsageRepository usage,
        IUsageProjectionPublisher publisher,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<UsageProjectionReconciler> logger,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _current = current;
        _usage = usage;
        _publisher = publisher;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
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

        var behind = candidates
            .Where(candidate =>
                counters.TryGetValue(candidate.ItemId, out var counter) &&
                counter.AppliedRecordCount > candidate.SourceVersion)
            .Select(candidate => candidate.SubscriptionId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

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
            _logger.LogWarning(
                "Repaired usage projections that were behind their counters " +
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

        if (string.IsNullOrWhiteSpace(work.AggregateId))
        {
            await _reconciler.SweepTenantAsync(work.TenantId, correlationId, cancellationToken);
        }
        else
        {
            await _reconciler.RefreshSubscriptionAsync(
                work.TenantId,
                work.AggregateId,
                correlationId,
                cancellationToken);
        }

        return SubscriptionWorkOutcome.Completed();
    }
}
