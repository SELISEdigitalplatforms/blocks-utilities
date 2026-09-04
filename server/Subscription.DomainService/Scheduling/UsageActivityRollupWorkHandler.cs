using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Folds the tenant's newly-recorded usage ledger entries into the analytics rollup collections.
/// </summary>
/// <remarks>
/// Always tenant-wide — this work type names no aggregate, since the rollup walks the ledger by
/// its own persisted cursor rather than by subscription. One batch per attempt: if the ledger
/// still holds more behind the cursor after this batch, the repair sweep's own due-check finds
/// that on its next pass and schedules another occurrence, the same way every other
/// cursor-driven recovery pass in this module works.
/// </remarks>
public sealed class UsageActivityRollupWorkHandler : ISubscriptionWorkHandler
{
    private readonly IUsageRollupService _rollups;

    public UsageActivityRollupWorkHandler(IUsageRollupService rollups) => _rollups = rollups;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.UsageActivityRollup;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var correlationId = string.IsNullOrWhiteSpace(work.CorrelationId)
            ? work.ItemId
            : work.CorrelationId;

        await _rollups.RunBatchAsync(work.TenantId, correlationId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}
