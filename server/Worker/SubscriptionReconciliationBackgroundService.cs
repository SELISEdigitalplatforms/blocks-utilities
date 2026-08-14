using Microsoft.Extensions.Options;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Utilities;

namespace Worker;

/// <summary>
/// The periodic sweep that finishes work no message could carry.
/// </summary>
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
    private readonly ILogger<SubscriptionReconciliationBackgroundService> _logger;

    public SubscriptionReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tenantIds = _options.CurrentValue.TenantIds;

        if (tenantIds.Length == 0)
        {
            // Said plainly, because the alternative is a service that looks healthy and
            // reconciles nothing. Nothing else discovers tenants.
            _logger.LogWarning(
                "Subscription reconciliation has no tenants configured and will do nothing. " +
                "Set Subscription:TenantIds to enable it");

            return;
        }

        _logger.LogInformation(
            "Subscription reconciliation started TenantCount={TenantCount}",
            tenantIds.Length);

        using var timer = new PeriodicTimer(PollInterval());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
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

            var activation = services.GetRequiredService<ISubscriptionActivationProcessor>();
            var outbox = services.GetRequiredService<ISubscriptionOutboxProcessor>();

            var settled = await activation.ProcessDueAsync(tenantId, stoppingToken);
            var recovered = await activation.RecoverStaleAsync(tenantId, stoppingToken);
            var published = await outbox.PublishDueAsync(tenantId, stoppingToken);

            if (settled + recovered + published > 0)
            {
                _logger.LogInformation(
                    "Subscription reconciliation pass completed SettledCount={SettledCount} " +
                    "RecoveredCount={RecoveredCount} PublishedCount={PublishedCount}",
                    settled,
                    recovered,
                    published);
            }
        }
    }

    private TimeSpan PollInterval() =>
        TimeSpan.FromSeconds(
            Math.Max(MinimumPollSeconds, _options.CurrentValue.ReconciliationPollSeconds));
}
