using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <inheritdoc cref="IUsageRollupService" />
public sealed class UsageRollupService : IUsageRollupService
{
    /// <summary>
    /// The named, persisted cursor this job walks the ledger forward on. Reuses
    /// <see cref="ISubscriptionDocumentCursorRepository"/> — the same mechanism the financial
    /// document recovery sweep already uses for its settled-charge and refund cursors — rather
    /// than the in-memory <c>UsageProjectionBackfillCursors</c>, which is deliberately
    /// non-persisted and appropriate only for a pass that is always safe to redo from the start.
    /// A tenant with a large ledger should not redo a full scan on every process restart.
    /// </summary>
    public const string RollupCursorName = "usage-activity-rollup";

    /// <summary>Bound on one backfill pass, so a runaway subscription cannot hold the ledger open.</summary>
    private const int BackfillRecordLimit = 50_000;

    private readonly ISubscriptionUsageRepository _usage;
    private readonly ISubscriptionUsageActivityRollupRepository _activity;
    private readonly ISubscriptionUsageActorRollupRepository _actors;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionDocumentCursorRepository _cursors;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<UsageRollupService> _logger;
    private readonly TimeProvider _time;

    public UsageRollupService(
        ISubscriptionUsageRepository usage,
        ISubscriptionUsageActivityRollupRepository activity,
        ISubscriptionUsageActorRollupRepository actors,
        ISubscriptionRepository subscriptions,
        ISubscriptionDocumentCursorRepository cursors,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<UsageRollupService> logger,
        TimeProvider? time = null)
    {
        _usage = usage;
        _activity = activity;
        _actors = actors;
        _subscriptions = subscriptions;
        _cursors = cursors;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> RunBatchAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var batch = Math.Max(1, options.UsageRollupBatchSize);

        var mark = await _cursors.GetAsync(tenantId, RollupCursorName, cancellationToken)
            ?? new FinancialDocumentSweepMark(
                _time.GetUtcNow().UtcDateTime.AddDays(
                    -Math.Max(1, options.UsageRollupFirstPassReachDays)),
                null);

        var records = await _usage.ListRecordedSinceAsync(
            tenantId,
            mark.ReadUpToUtc,
            mark.AfterId,
            batch,
            cancellationToken);

        if (records.Count == 0)
        {
            return 0;
        }

        // One subscription lookup per distinct subscription in the batch, not per record: a
        // batch of five hundred ledger entries ordinarily belongs to far fewer subscriptions than
        // that.
        var subscriptions = new Dictionary<string, SubscriptionDetail?>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            await ApplyRecordAsync(tenantId, record, subscriptions, cancellationToken);
        }

        var last = records[^1];

        await _cursors.SetAsync(
            tenantId,
            RollupCursorName,
            new FinancialDocumentSweepMark(last.RecordedAtUtc, last.ItemId),
            cancellationToken);

        _logger.LogInformation(
            "Usage rollup folded a batch of ledger records TenantHash={TenantHash} " +
            "RecordCount={RecordCount} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(tenantId),
            records.Count,
            correlationId);

        return records.Count;
    }

    public async Task<int> BackfillSubscriptionAsync(
        string tenantId,
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetByIdAsync(
            tenantId, subscriptionId, cancellationToken);

        var records = await _usage.ListRecordsAsync(
            tenantId,
            subscriptionId,
            meterKey: null,
            periodKey: null,
            BackfillRecordLimit,
            cancellationToken);

        var subscriptions = new Dictionary<string, SubscriptionDetail?>(StringComparer.Ordinal)
        {
            [subscriptionId] = subscription
        };

        foreach (var record in records)
        {
            await ApplyRecordAsync(tenantId, record, subscriptions, cancellationToken);
        }

        _logger.LogInformation(
            "Usage rollup backfilled one subscription from the ledger TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} RecordCount={RecordCount} " +
            "CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(tenantId),
            PaymentLogValue.Hash(subscriptionId),
            records.Count,
            correlationId);

        return records.Count;
    }

    private async Task ApplyRecordAsync(
        string tenantId,
        SubscriptionUsageRecord record,
        Dictionary<string, SubscriptionDetail?> subscriptionCache,
        CancellationToken cancellationToken)
    {
        if (!subscriptionCache.TryGetValue(record.SubscriptionId, out var subscription))
        {
            subscription = await _subscriptions.GetByIdAsync(
                tenantId, record.SubscriptionId, cancellationToken);
            subscriptionCache[record.SubscriptionId] = subscription;
        }

        // Bucketed by the day (and hour) the usage actually occurred, never by when it was
        // recorded: a record reported late must still land in the day it happened, which is why
        // the cursor above walks RecordedAtUtc while the bucket key below uses OccurredAtUtc.
        var occurred = DateTime.SpecifyKind(record.OccurredAtUtc, DateTimeKind.Utc);
        var dayUtc = occurred.Date;
        var updatedAtUtc = _time.GetUtcNow().UtcDateTime;

        await _activity.ApplyAsync(
            tenantId,
            record.OrganizationId,
            record.SubscriptionId,
            record.MeterKey,
            subscription?.Plan.PlanId ?? string.Empty,
            subscription?.Plan.Code ?? string.Empty,
            dayUtc,
            occurred.Hour,
            record.Delta,
            record.RecordedAtUtc,
            record.ItemId,
            updatedAtUtc,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(record.RecordedByUserId))
        {
            return;
        }

        await _actors.ApplyAsync(
            tenantId,
            record.OrganizationId,
            record.MeterKey,
            dayUtc,
            record.RecordedByUserId,
            record.Delta,
            record.RecordedAtUtc,
            record.ItemId,
            updatedAtUtc,
            cancellationToken);
    }
}
