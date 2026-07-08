using Sms.DomainService.Services;

namespace Sms.Worker.Consumers;

public class SmsBackgroundProcessingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SmsBackgroundProcessingService> _logger;

    public SmsBackgroundProcessingService(IServiceProvider serviceProvider, ILogger<SmsBackgroundProcessingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ISmsProcessingService>();
                await processor.ProcessDueRetriesAsync(stoppingToken);
                await processor.ReconcileSubmittedMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SmsBackgroundProcessingService: scheduled SMS background processing failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}

