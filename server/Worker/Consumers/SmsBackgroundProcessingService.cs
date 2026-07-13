using Sms.DomainService.Services;

namespace Sms.Worker.Consumers;

public class SmsBackgroundProcessingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SmsBackgroundProcessingService> _logger;
    private readonly IConfiguration _configuration;

    public SmsBackgroundProcessingService(IServiceProvider serviceProvider, ILogger<SmsBackgroundProcessingService> logger, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var tenantIds = GetConfiguredTenantIds();
                if (tenantIds.Count == 0)
                {
                    _logger.LogWarning("SmsBackgroundProcessingService: no tenant ids configured for scheduled SMS background processing.");
                    await DelayUntilNextRunAsync(stoppingToken);
                    continue;
                }

                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ISmsProcessingService>();
                foreach (var tenantId in tenantIds)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await processor.ProcessDueRetriesAsync(tenantId, stoppingToken);
                    await processor.ReconcileSubmittedMessagesAsync(tenantId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SmsBackgroundProcessingService: scheduled SMS background processing failed");
            }

            await DelayUntilNextRunAsync(stoppingToken);
        }
    }

    private async Task DelayUntilNextRunAsync(CancellationToken stoppingToken)
    {
        var delaySeconds = Math.Max(1, _configuration.GetValue<int?>("SmsBackgroundProcessing:PollIntervalSeconds") ?? 60);
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
    }

    private IReadOnlyList<string> GetConfiguredTenantIds()
    {
        return _configuration
            .GetSection("SmsBackgroundProcessing:TenantIds")
            .Get<string[]>()?
            .Where(tenantId => !string.IsNullOrWhiteSpace(tenantId))
            .Select(tenantId => tenantId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }
}
