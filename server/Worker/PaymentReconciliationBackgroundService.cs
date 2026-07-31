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










    }
}
