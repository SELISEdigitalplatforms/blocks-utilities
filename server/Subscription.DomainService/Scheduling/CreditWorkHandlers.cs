using Microsoft.Extensions.Options;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>Finishes or fails credit purchases a crash left part way. See spec 002.</summary>
public sealed class CreditPurchaseRecoveryWorkHandler : ISubscriptionWorkHandler
{
    private readonly ISubscriptionCreditService _credits;

    public CreditPurchaseRecoveryWorkHandler(ISubscriptionCreditService credits) => _credits = credits;

    public SubscriptionWorkType WorkType => SubscriptionWorkType.CreditPurchaseRecovery;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _credits.RecoverStaleAsync(work.TenantId, cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}

/// <summary>
/// Marks credit grants expired or forfeited once they have lapsed, one batch per run.
/// </summary>
/// <remarks>
/// Bookkeeping only. Spending already refuses a lapsed grant through
/// <see cref="CreditLedger.EffectiveStatus"/>, so a slow or failed run never lets credits be spent
/// that should not be; it only leaves the stored status behind for a while.
/// </remarks>
public sealed class CreditGrantMaintenanceWorkHandler : ISubscriptionWorkHandler
{
    private readonly CreditLedger _ledger;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly TimeProvider _time;

    public CreditGrantMaintenanceWorkHandler(
        CreditLedger ledger,
        IOptionsMonitor<SubscriptionOptions> options,
        TimeProvider? time = null)
    {
        _ledger = ledger;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public SubscriptionWorkType WorkType => SubscriptionWorkType.CreditGrantMaintenance;

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var options = _options.CurrentValue;

        await _ledger.MaintainAsync(
            work.TenantId,
            _time.GetUtcNow().UtcDateTime,
            TimeSpan.FromHours(Math.Max(1, options.CreditGrantCheckIntervalHours)),
            options.CreditMaintenanceBatchSize,
            cancellationToken);

        return SubscriptionWorkOutcome.Completed();
    }
}
