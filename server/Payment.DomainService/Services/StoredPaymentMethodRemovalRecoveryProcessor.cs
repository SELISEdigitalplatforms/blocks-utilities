using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class StoredPaymentMethodRemovalRecoveryProcessor :
    IStoredPaymentMethodRemovalRecoveryProcessor
{
    private readonly IStoredPaymentMethodRepository _methods;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly IStoredPaymentMethodProviderGatewayResolver
        _gatewayResolver;
    private readonly IProviderTokenProtector _tokenProtector;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<
        StoredPaymentMethodRemovalRecoveryProcessor> _logger;

    public StoredPaymentMethodRemovalRecoveryProcessor(
        IStoredPaymentMethodRepository methods,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        IStoredPaymentMethodProviderGatewayResolver gatewayResolver,
        IProviderTokenProtector tokenProtector,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<
            StoredPaymentMethodRemovalRecoveryProcessor> logger)
    {
        _methods = methods;
        _payments = payments;
        _providers = providers;
        _gatewayResolver = gatewayResolver;
        _tokenProtector = tokenProtector;
        _options = options;
        _logger = logger;
    }

    public async Task<int> RecoverDueRemovalsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var batchSize = Math.Clamp(
            options.WebhookBatchSize,
            1,
            200);
        var candidates =
            await _methods.GetDueRemovalCandidatesAsync(
                tenantId,
                DateTime.UtcNow,
                batchSize,
                cancellationToken);
        var recovered = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var leaseId = Guid.NewGuid().ToString("N");
            var leaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(
                Math.Clamp(
                    options.StoredPaymentMethodRemovalLeaseSeconds,
                    10,
                    300));
            var claimed = await _methods.TryClaimDueRemovalAsync(
                tenantId,
                candidate.ItemId,
                leaseId,
                leaseExpiresAtUtc,
                DateTime.UtcNow,
                cancellationToken);

            if (claimed == null)
            {
                continue;
            }

            if (!_tokenProtector.TryUnprotect(
                    claimed,
                    out var providerToken))
            {
                await MarkFailureAsync(
                    claimed,
                    leaseId,
                    "provider_token_unavailable",
                    cancellationToken);

                continue;
            }

            await MigrateLegacyTokenAsync(
                claimed,
                providerToken,
                cancellationToken);

            var provider = await GetProviderAsync(
                tenantId,
                claimed.ProviderName,
                cancellationToken);
            var gateway = _gatewayResolver.Resolve(
                claimed.ProviderName);

            if (provider == null ||
                !provider.IsEnabled ||
                gateway == null)
            {
                await MarkFailureAsync(
                    claimed,
                    leaseId,
                    "provider_unavailable",
                    cancellationToken);

                continue;
            }

            var outcome = await gateway.RemoveAsync(
                provider,
                claimed,
                providerToken,
                cancellationToken);
            providerToken = string.Empty;

            if (outcome ==
                StoredPaymentMethodRemovalOutcome.Removed)
            {
                if (await _methods.MarkRemovedAsync(
                        tenantId,
                        claimed.ItemId,
                        leaseId,
                        DateTime.UtcNow,
                        cancellationToken))
                {
                    recovered++;
                }

                continue;
            }

            await MarkFailureAsync(
                claimed,
                leaseId,
                outcome ==
                StoredPaymentMethodRemovalOutcome
                    .OperationalFailure
                    ? "provider_operational_failure"
                    : "provider_outcome_unknown",
                cancellationToken);
        }

        if (candidates.Count > 0)
        {
            _logger.LogInformation(
                "Stored payment method removal recovery completed TenantHash={TenantHash} CandidateCount={CandidateCount} RecoveredCount={RecoveredCount}",
                PaymentLogValue.Hash(tenantId),
                candidates.Count,
                recovered);
        }

        return recovered;
    }

    private async Task MarkFailureAsync(
        StoredPaymentMethod method,
        string leaseId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var attempts = method.RemovalAttemptCount + 1;
        var maximumAttempts = Math.Max(
            1,
            _options.CurrentValue
                .StoredPaymentMethodRemovalMaxAttempts);

        if (attempts >= maximumAttempts)
        {
            await _methods.MarkRemovalRequiresAttentionAsync(
                method.TenantId,
                method.ItemId,
                leaseId,
                errorCode,
                cancellationToken);

            _logger.LogError(
                "Stored payment method removal exhausted retries TenantHash={TenantHash} PaymentMethodHash={PaymentMethodHash} AttemptCount={AttemptCount}",
                PaymentLogValue.Hash(method.TenantId),
                PaymentLogValue.Hash(method.ItemId),
                attempts);

            return;
        }

        var delaySeconds = Math.Min(
            300,
            (int)Math.Pow(
                2,
                Math.Min(attempts, 8)) +
            Random.Shared.Next(0, 5));

        await _methods.MarkRemovalOutcomeUnknownAsync(
            method.TenantId,
            method.ItemId,
            leaseId,
            DateTime.UtcNow.AddSeconds(delaySeconds),
            errorCode,
            cancellationToken);
    }

    private Task<PaymentProvider?> GetProviderAsync(
        string tenantId,
        string providerName,
        CancellationToken cancellationToken) =>
        _providers.GetAsync(
            tenantId,
            providerName,
            () => _payments.GetProviderAsync(
                tenantId,
                providerName,
                cancellationToken));

    private async Task MigrateLegacyTokenAsync(
        StoredPaymentMethod method,
        string providerToken,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(
                method.ProviderTokenCiphertext) ||
            !_tokenProtector.TryProtect(
                providerToken,
                out var protectedToken))
        {
            return;
        }

        await _methods.MigrateLegacyTokenAsync(
            method.TenantId,
            method.ItemId,
            protectedToken,
            cancellationToken);
    }
}
