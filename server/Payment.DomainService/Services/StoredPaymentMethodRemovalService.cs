using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class StoredPaymentMethodRemovalService :
    IStoredPaymentMethodRemovalService
{
    private readonly IPaymentExecutionContextResolver _contexts;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly IShopperReferenceService _shopperReferences;
    private readonly IStoredPaymentMethodRepository _methods;
    private readonly IStoredPaymentMethodRateLimiter _rateLimiter;
    private readonly IPaymentDistributedLock _locks;
    private readonly IStoredPaymentMethodProviderGatewayResolver
        _gatewayResolver;
    private readonly IProviderTokenProtector _tokenProtector;
    private readonly IPaymentWorkDispatcher _workDispatcher;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StoredPaymentMethodRemovalService> _logger;
    private readonly TimeProvider _time;

    public StoredPaymentMethodRemovalService(
        IPaymentExecutionContextResolver contexts,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        IShopperReferenceService shopperReferences,
        IStoredPaymentMethodRepository methods,
        IStoredPaymentMethodRateLimiter rateLimiter,
        IPaymentDistributedLock locks,
        IStoredPaymentMethodProviderGatewayResolver gatewayResolver,
        IProviderTokenProtector tokenProtector,
        IPaymentWorkDispatcher workDispatcher,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StoredPaymentMethodRemovalService> logger,
        TimeProvider? time = null)
    {
        _contexts = contexts;
        _payments = payments;
        _providers = providers;
        _shopperReferences = shopperReferences;
        _methods = methods;
        _rateLimiter = rateLimiter;
        _locks = locks;
        _gatewayResolver = gatewayResolver;
        _tokenProtector = tokenProtector;
        _workDispatcher = workDispatcher;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<StoredPaymentMethodRemovalResult>
        RemoveStoredPaymentMethodAsync(
            string paymentMethodId,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var resolved = _contexts.Resolve(correlationId);

        if (!resolved.IsSuccess)
        {
            return Failure(resolved.Failure!);
        }

        var context = resolved.Context!;
        var rateLimit = await _rateLimiter.CheckRemovalAsync(
            context.TenantId,
            context.ActorId,
            cancellationToken);

        if (!rateLimit.IsAvailable)
        {
            return Unavailable(
                "payment_method_rate_limiter_unavailable",
                rateLimit);
        }

        if (!rateLimit.IsAllowed)
        {
            return new StoredPaymentMethodRemovalResult(
                StoredPaymentMethodRemovalStatus.Failed,
                PaymentFailureKind.RateLimited,
                "payment_method_rate_limit_exceeded",
                "Too many stored payment method removal requests.",
                rateLimit);
        }

        var method = await _methods.GetAsync(
            context.TenantId,
            paymentMethodId,
            cancellationToken);

        if (method == null)
        {
            return NotFound(rateLimit);
        }

        // From the card, so removal reaches the merchant account that issued its token.
        var provider = await GetProviderAsync(
            context.TenantId,
            method.OrganizationId,
            method.ProviderName,
            cancellationToken);

        if (provider == null ||
            !provider.IsEnabled ||
            !_shopperReferences.TryCreate(
                context.TenantId,
                context.ActorId,
                provider.ShopperReferenceHmacKey ?? string.Empty,
                out var shopperReference))
        {
            return Unavailable(
                "payment_provider_unavailable",
                rateLimit);
        }

        if (!FixedTimeEquals(
                method.ShopperReference,
                shopperReference))
        {
            return NotFound(rateLimit);
        }

        var terminal = ToExistingResult(
            method.Status,
            rateLimit);

        if (terminal != null)
        {
            return terminal;
        }

        if (await _payments.HasUnresolvedRecurringPaymentAsync(
                context.TenantId,
                method.ItemId,
                cancellationToken))
        {
            return new StoredPaymentMethodRemovalResult(
                StoredPaymentMethodRemovalStatus.Failed,
                PaymentFailureKind.Conflict,
                "payment_method_in_use",
                "The stored payment method has a payment in progress.",
                rateLimit);
        }

        await using var coordinationLock =
            await _locks.TryAcquireAsync(
                PaymentHashing.CreateLockResource(
                    context.TenantId,
                    $"stored-method:{paymentMethodId}"),
                cancellationToken);

        var leaseId = Guid.NewGuid().ToString("N");
        var leaseExpiresAtUtc = _time.GetUtcNow().UtcDateTime.AddSeconds(
            Math.Clamp(
                _options.CurrentValue
                    .StoredPaymentMethodRemovalLeaseSeconds,
                10,
                300));
        var claimed = await _methods.TryClaimRemovalAsync(
            context.TenantId,
            paymentMethodId,
            shopperReference,
            leaseId,
            leaseExpiresAtUtc,
            cancellationToken);

        if (claimed == null)
        {
            var current = await _methods.GetAsync(
                context.TenantId,
                paymentMethodId,
                cancellationToken);

            return current == null
                ? NotFound(rateLimit)
                : ToExistingResult(current.Status, rateLimit) ??
                  Pending(rateLimit);
        }

        var token = await _tokenProtector.UnprotectAsync(
            claimed,
            cancellationToken);
        var providerToken = token.ProviderToken;

        if (!token.IsRead)
        {
            await _methods.MarkRemovalRequiresAttentionAsync(
                context.TenantId,
                claimed.ItemId,
                leaseId,
                "provider_token_unavailable",
                cancellationToken);

            return Unavailable(
                "payment_method_token_unavailable",
                rateLimit);
        }

        await MigrateLegacyTokenAsync(
            claimed,
            providerToken,
            cancellationToken);

        var gateway = _gatewayResolver.Resolve(
            claimed.ProviderName);

        if (gateway == null)
        {
            await _methods.MarkRemovalRequiresAttentionAsync(
                context.TenantId,
                claimed.ItemId,
                leaseId,
                "provider_gateway_unavailable",
                cancellationToken);

            return Unavailable(
                "payment_provider_unavailable",
                rateLimit);
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
            var persisted = await _methods.MarkRemovedAsync(
                context.TenantId,
                claimed.ItemId,
                leaseId,
                _time.GetUtcNow().UtcDateTime,
                cancellationToken);

            return persisted
                ? Removed(rateLimit)
                : Pending(rateLimit);
        }

        if (outcome ==
            StoredPaymentMethodRemovalOutcome
                .OperationalFailure)
        {
            await _methods.MarkRemovalRequiresAttentionAsync(
                context.TenantId,
                claimed.ItemId,
                leaseId,
                "provider_operational_failure",
                cancellationToken);

            _logger.LogError(
                "Stored payment method removal requires attention TenantHash={TenantHash} PaymentMethodHash={PaymentMethodHash}",
                PaymentLogValue.Hash(context.TenantId),
                PaymentLogValue.Hash(claimed.ItemId));

            return Unavailable(
                "payment_method_removal_unavailable",
                rateLimit);
        }

        await _methods.MarkRemovalOutcomeUnknownAsync(
            context.TenantId,
            claimed.ItemId,
            leaseId,
            _time.GetUtcNow().UtcDateTime.AddSeconds(30),
            "provider_outcome_unknown",
            cancellationToken);

        await _workDispatcher.TryDispatchAsync(
            context.TenantId,
            includeRecovery: true,
            scheduledAtUtc:
                DateTimeOffset.UtcNow.AddSeconds(30),
            cancellationToken: cancellationToken);

        return Pending(rateLimit);
    }

    private Task<PaymentProvider?> GetProviderAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        CancellationToken cancellationToken) =>
        _providers.GetAsync(
            tenantId,
            organizationId,
            providerName,
            () => _payments.GetProviderAsync(
                tenantId,
                organizationId,
                providerName,
                cancellationToken));

    private async Task MigrateLegacyTokenAsync(
        StoredPaymentMethod method,
        string providerToken,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(method.ProviderTokenCiphertext))
        {
            return;
        }

        var protection = await _tokenProtector.ProtectAsync(
            PaymentEncryptionScope.From(method),
            providerToken,
            cancellationToken);

        if (!protection.IsProtected) return;

        await _methods.MigrateLegacyTokenAsync(
            method.TenantId,
            method.ItemId,
            protection.Token!,
            cancellationToken);
    }

    private static bool FixedTimeEquals(
        string left,
        string right)
    {
        var leftHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(left));
        var rightHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(right));

        return CryptographicOperations.FixedTimeEquals(
            leftHash,
            rightHash);
    }

    private static StoredPaymentMethodRemovalResult?
        ToExistingResult(
            PaymentMethodStatus status,
            PaymentRateLimitResult rateLimit) =>
        status switch
        {
            PaymentMethodStatus.Removed => Removed(rateLimit),
            PaymentMethodStatus.RemovalPending or
                PaymentMethodStatus.RemovalOutcomeUnknown =>
                Pending(rateLimit),
            PaymentMethodStatus.RemovalRequiresAttention =>
                Unavailable(
                    "payment_method_removal_requires_attention",
                    rateLimit),
            _ => null
        };

    private static StoredPaymentMethodRemovalResult Removed(
        PaymentRateLimitResult rateLimit) =>
        new(
            StoredPaymentMethodRemovalStatus.Removed,
            PaymentFailureKind.None,
            string.Empty,
            string.Empty,
            rateLimit);

    private static StoredPaymentMethodRemovalResult Pending(
        PaymentRateLimitResult rateLimit) =>
        new(
            StoredPaymentMethodRemovalStatus.Pending,
            PaymentFailureKind.None,
            string.Empty,
            string.Empty,
            rateLimit);

    private static StoredPaymentMethodRemovalResult NotFound(
        PaymentRateLimitResult rateLimit) =>
        new(
            StoredPaymentMethodRemovalStatus.Failed,
            PaymentFailureKind.NotFound,
            "payment_method_not_found",
            "The payment method was not found.",
            rateLimit);

    private static StoredPaymentMethodRemovalResult Unavailable(
        string code,
        PaymentRateLimitResult? rateLimit = null) =>
        new(
            StoredPaymentMethodRemovalStatus.Failed,
            PaymentFailureKind.Unavailable,
            code,
            "The stored payment method is temporarily unavailable.",
            rateLimit);

    private static StoredPaymentMethodRemovalResult Failure(
        PaymentOperationResult result) =>
        new(
            StoredPaymentMethodRemovalStatus.Failed,
            result.FailureKind,
            result.ErrorCode,
            result.ErrorMessage);
}
