using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// The periodic sweep for renewals: everything the fee schedule says is due right now, whether
/// that is a normal renewal or the next dunning retry.
/// </summary>
public sealed class SubscriptionRenewalProcessor : ISubscriptionRenewalProcessor
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionRenewalService _renewals;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionRenewalProcessor> _logger;
    private readonly TimeProvider _time;

    public SubscriptionRenewalProcessor(
        ISubscriptionRepository subscriptions,
        ISubscriptionRenewalService renewals,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionRenewalProcessor> logger,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _renewals = renewals;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> ProcessDueAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _subscriptions.ListDueForRenewalAsync(
            tenantId,
            now,
            Math.Max(1, options.RenewalBatchSize),
            cancellationToken);

        var renewed = 0;

        foreach (var subscription in due)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantHash"] = PaymentLogValue.Hash(tenantId),
                ["SubscriptionHash"] = PaymentLogValue.Hash(subscription.ItemId)
            });

            // An increase reserved but not yet settled means the quantity this renewal would price
            // is not the quantity the subscriber is about to hold. Billing a full period at the old
            // number and then granting the extra units — for a proration that covered the period
            // now ending — hands them over for free. Claim recovery runs immediately before this in
            // the same pass, so anything resolvable is already resolved; what is left is genuinely
            // unknown, and waiting a pass is cheaper than charging the wrong amount.
            if (subscription.SettlementReservation is not null)
            {
                _logger.LogWarning(
                    "Deferred a renewal while a quantity increase is unresolved " +
                    "ReservedAtUtc={ReservedAtUtc}",
                    subscription.SettlementReservation.ReservedAtUtc);

                continue;
            }

            // A lost compare-and-set inside RenewAsync (another worker got there first) is not
            // an error here — its outcome stands, and this pass simply moves on.
            await _renewals.RenewAsync(subscription, cancellationToken);
            renewed++;
        }

        return renewed;
    }
}
