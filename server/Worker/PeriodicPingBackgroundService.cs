using Microsoft.Extensions.Options;

namespace Worker
{
    public class PeriodConfiguration
    {
        public bool Enabled { get; set; }
        public string PingUrl { get; set; } = string.Empty;
        public int PingIntervalSeconds { get; set; }
    }

    public class PeriodicPingBackgroundService : BackgroundService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PeriodicPingBackgroundService> _logger;

        private PeriodicTimer? _timer;
        private readonly PeriodConfiguration _currentConfig;

        public PeriodicPingBackgroundService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<PeriodicPingBackgroundService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;

            _currentConfig = new PeriodConfiguration();
            _currentConfig.Enabled = _configuration.GetValue<bool>("PeriodicPingConfiguration:Enabled");
            _currentConfig.PingUrl = _configuration.GetValue<string>("PeriodicPingConfiguration:PingUrl") ?? string.Empty;
            _currentConfig.PingIntervalSeconds = _configuration.GetValue<int>("PeriodicPingConfiguration:PingIntervalInSeconds");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_currentConfig.Enabled)
            {
                _logger.LogInformation("Periodic ping is disabled.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentConfig.PingUrl))
            {
                _logger.LogWarning("Periodic ping is enabled but PingUrl is empty. Service will not start.");
                return;
            }

            ResetTimer();

            // 🔥 immediate first ping
            if (_currentConfig.Enabled && !string.IsNullOrWhiteSpace(_currentConfig.PingUrl))
            {
                await PingAsync(stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_currentConfig.Enabled || _timer == null)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                try
                {
                    await _timer.WaitForNextTickAsync(stoppingToken);
                    await PingAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Periodic ping failed");
                }
            }
        }

        private void ResetTimer()
        {
            _timer?.Dispose();

            if (_currentConfig.PingIntervalSeconds <= 0)
            {
                _timer = null;
                return;
            }

            _timer = new PeriodicTimer(
                TimeSpan.FromSeconds(_currentConfig.PingIntervalSeconds));
        }

        private async Task PingAsync(CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient();

            try
            {
                _logger.LogInformation("Pinging {Url}", _currentConfig.PingUrl);

                using var response = await client.GetAsync(_currentConfig.PingUrl, ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Ping success ({StatusCode})", response.StatusCode);
                    return;
                }

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    _logger.LogWarning(
                        "Ping failed with client error {StatusCode}. Check PingUrl.",
                        response.StatusCode);
                    return;
                }

                // 5xx – transient server issue
                _logger.LogError(
                    "Ping failed with server error {StatusCode}. Will retry later.",
                    response.StatusCode);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Ping timed out");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Ping request failed");
            }
        }
    }
}
