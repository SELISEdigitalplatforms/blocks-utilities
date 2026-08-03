using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Reports payment configuration that is missing or unusable, at startup.
/// </summary>
/// <remarks>
/// These settings are only read when a payment or a registration is attempted, so a deployment
/// missing one looks healthy until the first real request fails — and the failure surfaces as
/// an unhelpful "unavailable" to whoever tried. Naming the problem at boot puts it in front of
/// whoever deployed it instead.
/// </remarks>
public sealed class PaymentConfigurationReadinessLogger : IHostedService
{
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentConfigurationReadinessLogger> _logger;

    public PaymentConfigurationReadinessLogger(
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentConfigurationReadinessLogger> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        if (!SafeHttpsUrl.TryParse(options.PublicBaseUrl, out _))
        {
            _logger.LogError(
                "Payment:PublicBaseUrl is missing or not a usable HTTPS address Configured={Configured}. Registering a payment provider builds its return URL from it and will fail until it is set",
                string.IsNullOrWhiteSpace(options.PublicBaseUrl) ? "no" : "yes");
        }

        if (options.CurrencyMinorUnits.Count == 0)
        {
            _logger.LogError(
                "Payment:CurrencyMinorUnits is empty. Every payment will be rejected, because an amount cannot be converted without knowing the currency's minor units");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
