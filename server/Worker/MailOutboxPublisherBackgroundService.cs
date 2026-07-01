using Mail.DomainService.Mails;

namespace Worker
{
    public class MailOutboxPublisherBackgroundService : BackgroundService
    {
        private readonly ILogger<MailOutboxPublisherBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public MailOutboxPublisherBackgroundService(
            ILogger<MailOutboxPublisherBackgroundService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var outboxService = scope.ServiceProvider.GetRequiredService<IMailOutboxService>();
                    await outboxService.PublishPendingAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Mail outbox publisher loop failed.");
                }

                var delaySeconds = Math.Max(1, _configuration.GetValue<int?>("MailOutbox:PollIntervalSeconds") ?? 10);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            }
        }
    }
}
