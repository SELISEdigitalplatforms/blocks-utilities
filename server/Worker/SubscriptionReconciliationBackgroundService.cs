using Microsoft.Extensions.Options;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Worker;

/// <summary>
/// The repair sweep: finds subscription work that was never announced, and announces it.
/// </summary>
/// <remarks>
/// It <strong>never executes financial work</strong>. It walks the roster looking for tenants that
/// owe something, enqueues one idempotent occurrence per work type, and stops there; the queue
/// drainer runs it. This used to be able to execute the work itself, chosen by configuration, and
/// that is exactly the arrangement worth removing: two executors for one renewal is one renewal
/// charged twice, and which of them was live depended on a setting read separately by each.
/// <para>
/// Announcing is safe to repeat where executing is not. The occurrence index collapses this sweep's
/// announcement, the producer's at the point of change, and another replica's sweep onto one queue
/// item, so the worst case of announcing something already announced is a write that changes
/// nothing.
/// </para>
/// <para>
/// Deliberately slower than the queue poll. This is the path that costs a query per tenant, and it
/// exists for a case that is rare by construction — a tenant write that committed while the
/// scheduling write to the root database did not.
/// </para>
/// </remarks>
/// <remarks>
/// Activation normally happens within milliseconds of a webhook, driven by the payment work
/// command. This exists for the cases nothing dispatches: a compare-and-set lost to another
/// worker that then crashed, a charge raised but never recorded, a webhook that arrived while
/// this service was restarting.
/// <para>
/// Without a tick, a subscription in any of those states stays unpaid-looking forever while the
/// customer's money has already moved — the failure nobody reports because the customer assumes
/// it is their own doing.
/// </para>
/// </remarks>
public sealed class SubscriptionReconciliationBackgroundService : BackgroundService
{
    private const int MinimumPollSeconds = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly SubscriptionWorkMetrics _metrics;
    private readonly ILogger<SubscriptionReconciliationBackgroundService> _logger;

    public SubscriptionReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SubscriptionOptions> options,
        SubscriptionWorkMetrics metrics,
        ILogger<SubscriptionReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Subscription repair sweep started. It announces missing work to the durable queue " +
            "and never executes it.");

        using var timer = new PeriodicTimer(PollInterval());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                // Asked every pass, not captured at startup. Projects are created at any time
                // and can subscribe immediately; a roster read once is stale the moment the next
                // one appears, and a tenant this never visits is a tenant whose renewals never
                // happen. An empty answer is a quiet pass, never the end of the loop — on a
                // fresh environment it simply means nobody has signed up yet.
                using var directoryScope = _scopeFactory.CreateScope();
                var tenantIds = await directoryScope.ServiceProvider
                    .GetRequiredService<ISubscriptionTenantDirectory>()
                    .ListTenantIdsAsync(stoppingToken);

                await SweepAsync(tenantIds, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // One bad pass must not end the loop: the next tick is the recovery for
                // whatever went wrong here.
                _logger.LogError(
                    exception,
                    "Subscription reconciliation pass failed and will be retried");
            }
        }

        _logger.LogInformation("Subscription reconciliation stopped");
    }

    private async Task SweepAsync(
        IReadOnlyList<string> tenantIds,
        CancellationToken stoppingToken)
    {
        foreach (var tenantId in tenantIds)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await SweepTenantAsync(tenantId, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One tenant's failure ends that tenant's pass, never the sweep. The roster is
                // discovered rather than curated, so it will contain tenants this service cannot
                // reach — one never provisioned a database, one mid-migration. Letting that
                // escape would abort the loop at whatever position the bad tenant happens to
                // occupy and silently stop billing every tenant ordered after it.
                _logger.LogWarning(
                    exception,
                    "Subscription reconciliation skipped a tenant after an error " +
                    "TenantHash={TenantHash}",
                    PaymentLogValue.Hash(tenantId));
            }
        }
    }

    private async Task SweepTenantAsync(string tenantId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        // Background work has no request to read a tenant from, so one is established for
        // the duration — the same discipline the payment work consumer follows.
        using var context = services
            .GetRequiredService<IPaymentTenantContextScopeFactory>()
            .Establish(tenantId);

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantHash"] = PaymentLogValue.Hash(tenantId),
            ["SubscriptionSweepId"] = Guid.NewGuid().ToString("N")
        });

        // Announce, and only announce. The announcer holds no processor, so this path cannot
        // charge anybody — which is a stronger statement than "does not", and the reason the logic
        // lives in a class of its own rather than in a private method here.
        var announced = await services
            .GetRequiredService<SubscriptionRepairAnnouncer>()
            .AnnounceAsync(tenantId, stoppingToken);

        if (announced > 0)
        {
            _metrics.RecordRepairAnnouncements(announced);
        }
    }

    private TimeSpan PollInterval() =>
        TimeSpan.FromSeconds(
            Math.Max(MinimumPollSeconds, _options.CurrentValue.ReconciliationPollSeconds));
}
