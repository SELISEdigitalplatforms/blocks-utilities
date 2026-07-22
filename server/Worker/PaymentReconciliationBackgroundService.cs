using Microsoft.Extensions.Options;
using Payment.DomainService.Outbox;
using Payment.DomainService.Utilities;
using Payment.DomainService.Services;

namespace Worker;

public sealed class PaymentReconciliationBackgroundService :
    BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<
        PaymentReconciliationBackgroundService> _logger;

    public PaymentReconciliationBackgroundService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentReconciliationBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Payment reconciliation safety net started");

        //while (!stoppingToken.IsCancellationRequested)
        //{
        //    var options = _options.CurrentValue;
        //    var interval = TimeSpan.FromSeconds(
        //        Math.Clamp(
        //            options.ReconciliationPollSeconds,
        //            60,
        //            3600));

        //    try
        //    {
        //        await Task.Delay(interval, stoppingToken);
        //    }
        //    catch (OperationCanceledException)
        //        when (stoppingToken.IsCancellationRequested)
        //    {
        //        return;
        //    }

        //    var tenantIds = options.TenantIds
        //        .Where(x => !string.IsNullOrWhiteSpace(x))
        //        .Select(x => x.Trim())
        //        .Distinct(StringComparer.OrdinalIgnoreCase)
        //        .ToArray();
        //    try
        //    {
        //        if (tenantIds.Length == 0)
        //        {
        //            _logger.LogWarning(
        //                "Payment reconciliation has no configured tenant ids.");
        //        }
        //        else
        //        {
        //            foreach (var tenantId in tenantIds)
        //            {
        //                stoppingToken.ThrowIfCancellationRequested();

        //                var tenantHash = PaymentLogValue.Hash(tenantId);

        //                using var tenantLogScope = _logger.BeginScope(
        //                    new Dictionary<string, object?>
        //                    {
        //                        ["TenantHash"] = tenantHash,
        //                        ["PaymentReconciliationCycleId"] =
        //                            Guid.NewGuid().ToString("N")
        //                    });

        //                _logger.LogDebug(
        //                    "Payment reconciliation tenant cycle started TenantHash={TenantHash}",
        //                    tenantHash);

        //                using var scope = _serviceProvider.CreateScope();
        //                var contextFactory = scope.ServiceProvider
        //                    .GetRequiredService<
        //                        IPaymentTenantContextScopeFactory>();
        //                using var paymentContext =
        //                    contextFactory.Establish(tenantId);
        //                var webhooks = scope.ServiceProvider.GetRequiredService<IPaymentWebhookProcessor>();
        //                var processedWebhooks = await webhooks.ProcessDueAsync(
        //                    tenantId,
        //                    stoppingToken);

        //                var outbox = scope.ServiceProvider.GetRequiredService<IPaymentOutboxProcessor>();
        //                var publishedEvents = await outbox.PublishDueAsync(
        //                    tenantId,
        //                    stoppingToken);
        //                var refundOutbox = scope.ServiceProvider
        //                    .GetRequiredService<
        //                        IPaymentRefundOutboxProcessor>();
        //                var publishedRefundEvents =
        //                    await refundOutbox.PublishDueAsync(
        //                        tenantId,
        //                        stoppingToken);

        //                var recovery = scope.ServiceProvider
        //                    .GetRequiredService<
        //                        IPaymentRecoveryProcessor>();
        //                await recovery.RecoverStaleAsync(
        //                    tenantId,
        //                    stoppingToken);
        //                var methodRecovery = scope.ServiceProvider
        //                    .GetRequiredService<
        //                        IStoredPaymentMethodRemovalRecoveryProcessor>();
        //                await methodRecovery
        //                    .RecoverDueRemovalsAsync(
        //                        tenantId,
        //                        stoppingToken);
        //                var refundRecovery =
        //                    scope.ServiceProvider
        //                        .GetRequiredService<
        //                            IPaymentRefundRecoveryProcessor>();
        //                await refundRecovery.RecoverDueAsync(
        //                    tenantId,
        //                    stoppingToken);

        //                if (processedWebhooks > 0 ||
        //                    publishedEvents > 0 ||
        //                    publishedRefundEvents > 0)
        //                {
        //                    _logger.LogInformation(
        //                        "Payment reconciliation tenant cycle completed TenantHash={TenantHash} ProcessedWebhookCount={ProcessedWebhookCount} PublishedEventCount={PublishedEventCount} PublishedRefundEventCount={PublishedRefundEventCount}",
        //                        tenantHash,
        //                        processedWebhooks,
        //                        publishedEvents,
        //                        publishedRefundEvents);
        //                }
        //                else
        //                {
        //                    _logger.LogDebug(
        //                        "Payment reconciliation tenant cycle completed TenantHash={TenantHash} ProcessedWebhookCount=0 PublishedEventCount=0 PublishedRefundEventCount=0",
        //                        tenantHash);
        //                }
        //            }
        //        }
        //    }
        //    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        //    {
        //        return;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(
        //            ex,
        //            "Payment reconciliation cycle failed.");
        //    }
        //}
    }
}
