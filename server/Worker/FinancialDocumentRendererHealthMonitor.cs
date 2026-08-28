using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Worker;

/// <summary>
/// Keeps re-running the renderer probe while it is unhealthy, so document delivery reopens on its
/// own once Chromium comes back.
/// </summary>
/// <remarks>
/// Only probes while <see cref="IFinancialDocumentRendererHealth.IsHealthy"/> is false. A renderer
/// that is fine has nothing for this loop to do — the startup check already recorded success, and
/// polling a working dependency on a timer would be pure cost for a signal nothing downstream
/// needs. It starts checking every tick once the gate turns false, which is also how a probe that
/// was healthy at startup and later breaks gets noticed without a separate watchdog for that case.
/// </remarks>
public sealed class FinancialDocumentRendererHealthMonitor : BackgroundService
{
    private readonly IFinancialDocumentPdfRenderer _renderer;
    private readonly IFinancialDocumentRendererHealth _health;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<FinancialDocumentRendererHealthMonitor> _logger;

    public FinancialDocumentRendererHealthMonitor(
        IFinancialDocumentPdfRenderer renderer,
        IFinancialDocumentRendererHealth health,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<FinancialDocumentRendererHealthMonitor> logger)
    {
        _renderer = renderer;
        _health = health;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(
                Math.Max(1, _options.CurrentValue.RendererHealthProbeIntervalSeconds));

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (_health.IsHealthy)
            {
                // Nothing broke since the last tick — the common case for the whole lifetime of a
                // healthy process. Re-probing here would cost a browser page and prove nothing the
                // gate does not already know.
                continue;
            }

            try
            {
                await FinancialDocumentRendererProbe.RunAsync(
                    _renderer, _health, _logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The probe itself already turns a renderer failure into RecordFailure rather than
                // an exception — this only catches something outside that, and it must not stop
                // the loop, or a broken renderer would also be a renderer that never gets
                // re-checked.
                _logger.LogError(exception, "The PDF renderer health probe itself failed to run");
            }
        }
    }
}
