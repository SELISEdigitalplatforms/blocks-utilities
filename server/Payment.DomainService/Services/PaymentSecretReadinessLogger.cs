using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Payment.DomainService.Services;

public sealed class PaymentSecretReadinessLogger : IHostedService
{
    private readonly PaymentSecretReadiness _readiness;
    private readonly ILogger<PaymentSecretReadinessLogger> _logger;

    public PaymentSecretReadinessLogger(
        PaymentSecretReadiness readiness,
        ILogger<PaymentSecretReadinessLogger> logger)
    {
        _readiness = readiness;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_readiness.IsProviderTokenEncryptionAvailable)
        {
            _logger.LogError(
                "Payment provider-token encryption is unavailable FailureCode={FailureCode}. The host will continue running, but payment operations that require provider-token encryption will fail closed until the Key Vault secret is configured and the host is restarted",
                _readiness.FailureCode);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
