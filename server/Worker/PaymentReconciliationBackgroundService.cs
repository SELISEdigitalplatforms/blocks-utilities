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
                foreach (var tenantId in await _tenants.ListTenantIdsAsync(stoppingToken))
                {
                    await ReconcileTenantAsync(tenantId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Payment reconciliation pass failed and will retry");
            }
        }
    }

    private async Task ReconcileTenantAsync(string tenantId, CancellationToken token)
    {
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
        await services.GetRequiredService<IPaymentWebhookProcessor>()
            .ProcessDueAsync(tenantId, token);
        await services.GetRequiredService<IStoredPaymentMethodRemovalRecoveryProcessor>()
            .RecoverDueRemovalsAsync(tenantId, token);
        await services.GetRequiredService<IPaymentOutboxProcessor>()
            .PublishDueAsync(tenantId, token);
        await services.GetRequiredService<IPaymentRefundOutboxProcessor>()
            .PublishDueAsync(tenantId, token);
    }

    private TimeSpan PollInterval() => TimeSpan.FromSeconds(
        Math.Max(30, _options.CurrentValue.ReconciliationPollSeconds));
}
