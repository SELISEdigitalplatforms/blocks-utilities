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
            //while (!stoppingToken.IsCancellationRequested)
            //{
            //    try
            //    {
            //        if (!_configuration.GetValue("MailOutbox:SweepEnabled", false))
            //        {
            //            await DelayUntilNextSweepAsync(stoppingToken);
            //            continue;
            //        }

            //        using var scope = _serviceProvider.CreateScope();
            //        var outboxService = scope.ServiceProvider.GetRequiredService<IMailOutboxService>();
            //        var tenantIds = GetConfiguredTenantIds();

            //        if (tenantIds.Count == 0)
            //        {
            //            _logger.LogWarning("Mail outbox publisher is running without configured tenant ids. Falling back to default database context.");
            //            await outboxService.PublishPendingAsync(stoppingToken);
            //        }
            //        else
            //        {
            //            foreach (var tenantId in tenantIds)
            //            {
            //                stoppingToken.ThrowIfCancellationRequested();
            //                await outboxService.PublishPendingAsync(tenantId, stoppingToken);
            //            }
            //        }
            //    }
            //    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            //    {
            //        return;
            //    }
            //    catch (Exception ex)
            //    {
            //        _logger.LogError(ex, "Mail outbox publisher loop failed.");
            //    }

            //    await DelayUntilNextSweepAsync(stoppingToken);
            //}
        }

        private async Task DelayUntilNextSweepAsync(CancellationToken stoppingToken)
        {
            var delaySeconds = Math.Max(1, _configuration.GetValue<int?>("MailOutbox:PollIntervalSeconds") ?? 10);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }

        private IReadOnlyList<string> GetConfiguredTenantIds()
        {
            return _configuration
                .GetSection("MailOutbox:TenantIds")
                .Get<string[]>()?
                .Where(tenantId => !string.IsNullOrWhiteSpace(tenantId))
                .Select(tenantId => tenantId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
        }
    }
}
