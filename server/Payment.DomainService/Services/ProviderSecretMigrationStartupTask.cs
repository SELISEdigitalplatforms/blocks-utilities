using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Runs the credential migration once at startup when it is switched on.
/// </summary>
/// <remarks>
/// A startup task rather than an endpoint: this reads every provider credential a tenant has,
/// so it is not something to leave reachable over HTTP. Failures are logged rather than thrown,
/// because a provider that cannot be migrated should not stop the service from starting and
/// serving every other provider.
/// </remarks>
public sealed class ProviderSecretMigrationStartupTask : BackgroundService
{
    private readonly IProviderSecretMigrationService _migration;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<ProviderSecretMigrationStartupTask> _logger;

    public ProviderSecretMigrationStartupTask(
        IProviderSecretMigrationService migration,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<ProviderSecretMigrationStartupTask> logger)
    {
        _migration = migration;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;

        if (!options.MigrateProviderSecretsOnStartup)
        {
            return;
        }

        if (options.TenantIds.Length == 0)
        {
            _logger.LogWarning(
                "Provider secret migration is enabled but no tenants are configured; nothing to do.");

            return;
        }

        _logger.LogInformation(
            "Provider secret migration starting TenantCount={TenantCount}",
            options.TenantIds.Length);

        foreach (var tenantId in options.TenantIds)
        {
            if (stoppingToken.IsCancellationRequested) return;

            try
            {
                var summary = await _migration.MigrateAsync(tenantId, stoppingToken);

                if (summary.Failed > 0)
                {
                    _logger.LogError(
                        "Provider secret migration left providers unusable TenantHash={TenantHash} Failed={Failed}; those providers will not accept payments until resolved",
                        PaymentLogValue.Hash(tenantId),
                        summary.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Provider secret migration failed for a tenant TenantHash={TenantHash} ExceptionType={ExceptionType}",
                    PaymentLogValue.Hash(tenantId),
                    exception.GetType().Name);
            }
        }
    }
}
