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
        // The loop below is commented out, so this service does nothing. It previously
        // announced itself as started, which read in the logs as the safety net running.
        // Payments stuck between a committed write and a failed dispatch stay stuck until
        // someone notices them by hand.
        _logger.LogWarning(
            "Payment reconciliation safety net is DISABLED. Payments left behind by a failed work dispatch will not be recovered automatically");










    }
}
