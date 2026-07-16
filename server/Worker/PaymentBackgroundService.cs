using Microsoft.Extensions.Options;
using Payment.DomainService.Outbox;
using Payment.DomainService.Utilities;
using Payment.DomainService.Services;

namespace Worker;

public sealed class PaymentBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentBackgroundService> _logger;

    public PaymentBackgroundService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRecoveryAtUtc = DateTime.MinValue;

        _logger.LogInformation(
            "Payment background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            var tenantIds = options.TenantIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var runRecovery = DateTime.UtcNow >= nextRecoveryAtUtc;
            try
            {
                if (tenantIds.Length == 0)
                {
                    _logger.LogWarning("Payment background processing has no configured tenant ids.");
                }
                else
                {
                    foreach (var tenantId in tenantIds)
                    {
                        stoppingToken.ThrowIfCancellationRequested();

                        var tenantHash = PaymentLogValue.Hash(tenantId);

                        using var tenantLogScope = _logger.BeginScope(
                            new Dictionary<string, object?>
                            {
                                ["TenantHash"] = tenantHash,
                                ["PaymentWorkerCycleId"] = Guid.NewGuid().ToString("N")
                            });

                        _logger.LogDebug(
                            "Payment background tenant cycle started TenantHash={TenantHash} RunRecovery={RunRecovery}",
                            tenantHash,
                            runRecovery);

                        using var scope = _serviceProvider.CreateScope();
                        var webhooks = scope.ServiceProvider.GetRequiredService<IPaymentWebhookProcessor>();
                        var processedWebhooks = await webhooks.ProcessDueAsync(
                            tenantId,
                            stoppingToken);

                        var outbox = scope.ServiceProvider.GetRequiredService<IPaymentOutboxProcessor>();
                        var publishedEvents = await outbox.PublishDueAsync(
                            tenantId,
                            stoppingToken);

                        if (runRecovery)
                        {
                            var recovery = scope.ServiceProvider.GetRequiredService<IPaymentRecoveryProcessor>();
                            await recovery.RecoverStaleAsync(tenantId, stoppingToken);
                            var methodRecovery = scope.ServiceProvider.GetRequiredService<IStoredPaymentMethodRecoveryProcessor>();
                            await methodRecovery.RecoverAsync(tenantId, stoppingToken);
                        }

                        if (processedWebhooks > 0 || publishedEvents > 0)
                        {
                            _logger.LogInformation(
                                "Payment background tenant cycle completed TenantHash={TenantHash} ProcessedWebhookCount={ProcessedWebhookCount} PublishedEventCount={PublishedEventCount} RecoveryExecuted={RecoveryExecuted}",
                                tenantHash,
                                processedWebhooks,
                                publishedEvents,
                                runRecovery);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Payment background tenant cycle completed TenantHash={TenantHash} ProcessedWebhookCount=0 PublishedEventCount=0 RecoveryExecuted={RecoveryExecuted}",
                                tenantHash,
                                runRecovery);
                        }
                    }
                    if (runRecovery)
                        nextRecoveryAtUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(options.RecoveryPollSeconds, 5, 900));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Payment background processing cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.OutboxPollSeconds, 1, 300)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
