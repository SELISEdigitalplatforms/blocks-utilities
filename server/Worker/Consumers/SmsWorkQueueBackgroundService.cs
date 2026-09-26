using Sms.DomainService.Scheduling;
using Sms.DomainService.Services;
using Sms.DomainService.Utilities;

namespace Sms.Worker.Consumers;

/// <summary>
/// Drains the root-database SMS queue: retries and delivery checks for every tenant, found by due
/// time rather than by walking tenants.
/// </summary>
public class SmsWorkQueueBackgroundService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    private readonly ISmsWorkQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmsWorkQueueBackgroundService> _logger;

    public SmsWorkQueueBackgroundService(ISmsWorkQueue queue, IServiceScopeFactory scopeFactory, ILogger<SmsWorkQueueBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            SmsBackgroundWork? work;
            try
            {
                work = await _queue.ClaimNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SmsWorkQueue: claim failed; root database unreachable?");
                await Task.Delay(ErrorDelay, stoppingToken);
                continue;
            }

            if (work == null)
            {
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            // ponytail: one item at a time per replica; add bounded parallelism if the backlog grows.
            await RunAsync(work, stoppingToken);
        }
    }

    private async Task RunAsync(SmsBackgroundWork work, CancellationToken stoppingToken)
    {
        using var logScope = SmsLogScope.Begin(_logger, work.TenantId, work.CorrelationId, work.MessageId);

        try
        {
            using (SmsTenantContext.Enter(work.TenantId))
            using (var scope = _scopeFactory.CreateScope())
            {
                var processing = scope.ServiceProvider.GetRequiredService<ISmsProcessingService>();
                var task = work.Kind switch
                {
                    SmsWorkKind.Retry => processing.ProcessSendAsync(work.TenantId, work.MessageId, work.CorrelationId, stoppingToken),
                    SmsWorkKind.DeliveryCheck => processing.CheckDeliveryAsync(work.TenantId, work.MessageId, stoppingToken),
                    _ => throw new InvalidOperationException($"Unknown SMS work kind '{work.Kind}'.")
                };
                await task;
            }

            await _queue.CompleteAsync(work, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The lease lapses and another replica picks the item up.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SmsWorkQueue: {Kind} failed Failures={Failures}", work.Kind, work.FailureCount + 1);
            await _queue.FailAsync(work, ex.Message, CancellationToken.None);
        }
    }
}
