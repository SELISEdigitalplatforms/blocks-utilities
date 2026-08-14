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

        foreach (var subscription in due)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantHash"] = PaymentLogValue.Hash(tenantId),
                ["SubscriptionHash"] = PaymentLogValue.Hash(subscription.ItemId)
            });

            // A lost compare-and-set inside RenewAsync (another worker got there first) is not
            // an error here — its outcome stands, and this pass simply moves on.
            await _renewals.RenewAsync(subscription, cancellationToken);
        }

        return due.Count;
    }
}
