using Microsoft.Extensions.Options;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Services;
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
        _logger.LogInformation("Subscription reconciliation started");

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

        var activation = services.GetRequiredService<ISubscriptionActivationProcessor>();
        var renewals = services.GetRequiredService<ISubscriptionRenewalProcessor>();
        var quantityClaims = services
            .GetRequiredService<ISubscriptionSettlementReservationProcessor>();
        var usageRating = services.GetRequiredService<ISubscriptionUsageRatingProcessor>();
        var outbox = services.GetRequiredService<ISubscriptionOutboxProcessor>();

        var settled = await activation.ProcessDueAsync(tenantId, stoppingToken);
        var recovered = await activation.RecoverStaleAsync(tenantId, stoppingToken);
        // Before renewals, deliberately. A renewal prices the period ahead from the quantities
        // on the subscription, so an increase still sitting unresolved would have the next period
        // billed at the old quantity and the units granted afterwards for nothing.
        var claimsResolved = await quantityClaims.RecoverStaleAsync(tenantId, stoppingToken);
        var renewed = await renewals.ProcessDueAsync(tenantId, stoppingToken);
        var periodsClosed = await usageRating.CloseDuePeriodsAsync(tenantId, stoppingToken);
        var invoicesCharged = await usageRating.ChargeDueInvoicesAsync(tenantId, stoppingToken);
        var published = await outbox.PublishDueAsync(tenantId, stoppingToken);

        if (settled + recovered + renewed + claimsResolved +
            periodsClosed + invoicesCharged + published > 0)
        {
            _logger.LogInformation(
                "Subscription reconciliation pass completed SettledCount={SettledCount} " +
                "RecoveredCount={RecoveredCount} RenewedCount={RenewedCount} " +
                "QuantityClaimsResolvedCount={QuantityClaimsResolvedCount} " +
                "UsagePeriodsClosedCount={UsagePeriodsClosedCount} " +
                "UsageInvoicesChargedCount={UsageInvoicesChargedCount} " +
                "PublishedCount={PublishedCount}",
                settled,
                recovered,
                renewed,
                claimsResolved,
                periodsClosed,
                invoicesCharged,
                published);
        }
    }

    private TimeSpan PollInterval() =>
        TimeSpan.FromSeconds(
            Math.Max(MinimumPollSeconds, _options.CurrentValue.ReconciliationPollSeconds));
}
