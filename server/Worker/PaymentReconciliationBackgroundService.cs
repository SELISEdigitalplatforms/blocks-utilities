using Microsoft.Extensions.Options;
using Payment.DomainService.Outbox;
using Payment.DomainService.Scheduling;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Worker;

/// <summary>The legacy direct repair path, retained while the durable queue is rolled out.</summary>
public sealed class PaymentReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentReconciliationBackgroundService> _logger;
    private readonly PaymentSchedulerMode? _mode;
    private readonly IPaymentWorkTenantSource? _tenants;

    public PaymentReconciliationBackgroundService(
        IServiceProvider services,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentReconciliationBackgroundService> logger,
        PaymentSchedulerMode? mode = null,
        IPaymentWorkTenantSource? tenants = null)
    {
        _services = services;
        _options = options;
        _logger = logger;
        _mode = mode;
        _tenants = tenants;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_mode?.QueueDriven == true)
        {
            _logger.LogInformation("Payment direct reconciliation is idle because the durable queue owns recovery");
            return;
        }

        if (_tenants is null)
        {
            _logger.LogWarning("Payment reconciliation has no tenant source and cannot run");
            return;
        }

        _logger.LogWarning("Payment reconciliation is running in DIRECT repair mode");
        using var timer = new PeriodicTimer(PollInterval());

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReconcilePassAsync(_tenants, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Only the roster read lands here now; a tenant's failure is handled per tenant.
                _logger.LogError(exception, "Payment reconciliation pass failed and will retry");
            }
        }
    }

    /// <summary>One pass over the tenant roster. Internal so a test can run a pass without the timer.</summary>
    internal async Task ReconcilePassAsync(IPaymentWorkTenantSource tenants, CancellationToken stoppingToken)
    {
        foreach (var tenantId in await tenants.ListTenantIdsAsync(stoppingToken))
        {
            try
            {
                await ReconcileTenantAsync(tenantId, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One tenant's failure ends that tenant's pass, never the sweep. The roster is
                // discovered rather than curated, so it contains tenants with no database here;
                // letting that escape aborted every pass at the first such tenant, and every
                // tenant ordered after it was never swept (seen in production on 2026-10-07,
                // where the queue had been the only thing reaching them). Same rule as the
                // subscription sweep: an unprovisioned tenant gets one line, anything else its trace.
                if (exception.GetBaseException() is KeyNotFoundException)
                {
                    _logger.LogWarning(
                        "Payment reconciliation skipped a tenant with no database TenantId={TenantId}",
                        PaymentLogValue.Id(tenantId));
                }
                else
                {
                    _logger.LogWarning(
                        exception,
                        "Payment reconciliation skipped a tenant after an error TenantId={TenantId}",
                        PaymentLogValue.Id(tenantId));
                }
            }
        }
    }

    private async Task ReconcileTenantAsync(string tenantId, CancellationToken token)
    {
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = PaymentLogValue.Id(tenantId)
        });
        using var scope = _services.CreateScope();
        using var tenant = scope.ServiceProvider
            .GetRequiredService<IPaymentTenantContextScopeFactory>()
            .Establish(tenantId);
        var services = scope.ServiceProvider;

        await services.GetRequiredService<IPaymentRecoveryProcessor>()
            .RecoverStaleAsync(tenantId, token);
        await services.GetRequiredService<IPaymentCaptureRecoveryProcessor>()
            .RecoverDueAsync(tenantId, token);
        await services.GetRequiredService<IPaymentRefundRecoveryProcessor>()
            .RecoverDueAsync(tenantId, token);
        // Result discarded: the targeted subscription settle rides the webhook-driven work
        // command, and this periodic reconciliation pass has the repair sweep behind it.
        _ = await services.GetRequiredService<IPaymentWebhookProcessor>()
            .ProcessDueAsync(tenantId, token);
        await services.GetRequiredService<IStoredPaymentMethodRemovalRecoveryProcessor>()
            .RecoverDueRemovalsAsync(tenantId, token);
        await services.GetRequiredService<IPaymentMethodSetupExpiryProcessor>()
            .ExpireDueAsync(tenantId, token);
        await services.GetRequiredService<IPaymentOutboxProcessor>()
            .PublishDueAsync(tenantId, token);
        await services.GetRequiredService<IPaymentRefundOutboxProcessor>()
            .PublishDueAsync(tenantId, token);
    }

    private TimeSpan PollInterval() => TimeSpan.FromSeconds(
        Math.Max(30, _options.CurrentValue.ReconciliationPollSeconds));
}
