using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <inheritdoc />
public sealed class SubscriptionTenantDirectory : ISubscriptionTenantDirectory
{
    private readonly ISubscriptionTenantSource _source;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionTenantDirectory> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<string> _tenantIds = [];
    private DateTimeOffset? _refreshedAt;

    public SubscriptionTenantDirectory(
        ISubscriptionTenantSource source,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionTenantDirectory> logger,
        TimeProvider? time = null)
    {
        _source = source;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<string>> ListTenantIdsAsync(
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        // A configured list wins outright and never reaches the registry. This is the escape
        // hatch: pinning one tenant locally, or overriding discovery if it ever misbehaves in an
        // environment where billing cannot wait for a fix.
        if (options.TenantIds.Length > 0)
        {
            return options.TenantIds;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!IsStale(options))
            {
                return _tenantIds;
            }

            var discovered = await _source.ListTenantIdsAsync(cancellationToken);

            // Only after a successful read, so a failure leaves the previous list in place
            // rather than aging into an empty one.
            _refreshedAt = _time.GetUtcNow();

            if (discovered.Count == 0)
            {
                // Said out loud every refresh. An empty registry is legitimate on a fresh
                // environment, but it is indistinguishable from a misconfigured one, and the
                // symptom of the second is billing that quietly never runs.
                _logger.LogWarning(
                    "Subscription reconciliation discovered no tenants and will sweep nothing " +
                    "this pass");
            }
            else if (discovered.Count != _tenantIds.Count)
            {
                _logger.LogInformation(
                    "Subscription tenant roster refreshed TenantCount={TenantCount} " +
                    "PreviousTenantCount={PreviousTenantCount}",
                    discovered.Count,
                    _tenantIds.Count);
            }

            _tenantIds = discovered;

            return _tenantIds;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Sweeping nothing because the registry was briefly unreachable is the worst
            // outcome available: billing stops while the service goes on looking healthy. The
            // last known roster is stale at worst, and a tenant list does not change quickly.
            _logger.LogWarning(
                exception,
                "Subscription tenant roster could not be refreshed; continuing with the last " +
                "known TenantCount={TenantCount}",
                _tenantIds.Count);

            return _tenantIds;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsStale(SubscriptionOptions options) =>
        _refreshedAt is not { } refreshedAt ||
        _time.GetUtcNow() - refreshedAt >=
            TimeSpan.FromSeconds(Math.Max(1, options.TenantRefreshSeconds));
}
