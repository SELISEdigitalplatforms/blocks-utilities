using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class StoredPaymentMethodService : IStoredPaymentMethodService, IStoredPaymentMethodRecoveryProcessor
{
    private readonly IPaymentExecutionContextResolver _contexts;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly IShopperReferenceService _shopperReferences;
    private readonly IStoredPaymentMethodRepository _methods;
    private readonly IPaymentDistributedLock _locks;
    private readonly IStoredPaymentMethodProviderClient _client;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<StoredPaymentMethodService> _logger;

    public StoredPaymentMethodService(
        IPaymentExecutionContextResolver contexts,
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        IShopperReferenceService shopperReferences,
        IStoredPaymentMethodRepository methods,
        IPaymentDistributedLock locks,
        IStoredPaymentMethodProviderClient client,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<StoredPaymentMethodService> logger)
    {
        _contexts = contexts;
        _payments = payments;
        _providers = providers;
        _shopperReferences = shopperReferences;
        _methods = methods;
        _locks = locks;
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<StoredPaymentMethodOperationResult> ListAsync(string correlationId, CancellationToken cancellationToken)
    {
        var resolved = _contexts.Resolve(correlationId);
        if (!resolved.IsSuccess) return Failed(resolved.Failure!);
        var context = resolved.Context!;
        var provider = await GetProviderAsync(context.TenantId, cancellationToken);
        if (provider == null || !_shopperReferences.TryCreate(context.TenantId, context.ActorId,
                provider.ShopperReferenceHmacKey ?? string.Empty, out var shopper)) return Unavailable();
        var methods = await _methods.ListActiveAsync(context.TenantId, shopper, cancellationToken);
        return new StoredPaymentMethodOperationResult(true, methods.Select(Map).ToArray(), PaymentFailureKind.None, string.Empty, string.Empty);
    }

    public async Task<StoredPaymentMethodOperationResult> DeleteAsync(string paymentMethodId, string correlationId, CancellationToken cancellationToken)
    {
        var resolved = _contexts.Resolve(correlationId);
        if (!resolved.IsSuccess) return Failed(resolved.Failure!);
        var context = resolved.Context!;
        var provider = await GetProviderAsync(context.TenantId, cancellationToken);
        if (provider == null || !_shopperReferences.TryCreate(context.TenantId, context.ActorId,
                provider.ShopperReferenceHmacKey ?? string.Empty, out var shopper)) return Unavailable();
        var method = await _methods.GetAsync(context.TenantId, paymentMethodId, cancellationToken);
        if (method == null || !string.Equals(method.ShopperReference, shopper, StringComparison.Ordinal))
            return new(false, null, PaymentFailureKind.NotFound, "payment_method_not_found", "The payment method was not found.");
        if (method.Status == PaymentMethodStatus.Disabled) return Success();

        await using var coordination = await _locks.TryAcquireAsync(
            PaymentHashing.CreateLockResource(context.TenantId, $"method:{paymentMethodId}"), cancellationToken);
        method = await _methods.GetAsync(context.TenantId, paymentMethodId, cancellationToken);
        if (method?.Status == PaymentMethodStatus.Disabled) return Success();
        var outcome = await _client.DeleteAsync(provider, method!, cancellationToken);
        if (outcome == ProviderClientOutcome.Success)
        {
            await _methods.MarkDisabledAsync(context.TenantId, paymentMethodId, NextAuthoritativeTimestamp(method!), cancellationToken);
            return Success();
        }
        await _methods.MarkDeletionUnknownAsync(context.TenantId, paymentMethodId, DateTime.UtcNow.AddSeconds(30), cancellationToken);
        return new(false, null,
            outcome == ProviderClientOutcome.Timeout ? PaymentFailureKind.Timeout : PaymentFailureKind.Unavailable,
            "payment_method_deletion_unknown",
            "The deletion outcome is pending confirmation.");
    }

    public async Task<int> RecoverAsync(string tenantId, CancellationToken cancellationToken)
    {
        var due = await _methods.GetUnknownDeletionsAsync(tenantId, DateTime.UtcNow,
            Math.Clamp(_options.CurrentValue.WebhookBatchSize, 1, 200), cancellationToken);
        var provider = await GetProviderAsync(tenantId, cancellationToken);
        if (provider == null) return 0;
        var recovered = 0;
        foreach (var method in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var coordination = await _locks.TryAcquireAsync(
                PaymentHashing.CreateLockResource(tenantId, $"method:{method.ItemId}"), cancellationToken);
            var outcome = await _client.DeleteAsync(provider, method, cancellationToken);
            if (outcome == ProviderClientOutcome.Success)
            {
                await _methods.MarkDisabledAsync(tenantId, method.ItemId, NextAuthoritativeTimestamp(method), cancellationToken);
                recovered++;
            }
            else
            {
                var delay = Math.Min(300, (int)Math.Pow(2, Math.Min(method.DeletionAttemptCount + 1, 8)) + Random.Shared.Next(0, 5));
                await _methods.MarkDeletionUnknownAsync(tenantId, method.ItemId, DateTime.UtcNow.AddSeconds(delay), cancellationToken);
            }
        }
        if (due.Count > 0) _logger.LogInformation("Processed stored payment method deletion recovery TenantHash={TenantHash} Count={Count}",
            PaymentHashing.HashSensitiveValue(tenantId)[..16], due.Count);
        return recovered;
    }

    private Task<PaymentProvider?> GetProviderAsync(string tenantId, CancellationToken cancellationToken) =>
        _providers.GetAsync(tenantId, PaymentConstants.AdyenOnlineProvider,
            () => _payments.GetProviderAsync(tenantId, PaymentConstants.AdyenOnlineProvider, cancellationToken));
    private static StoredPaymentMethodResponse Map(StoredPaymentMethod method) => new()
    {
        PaymentMethodId = method.ItemId,
        Type = method.Type,
        Brand = method.Brand,
        LastFour = method.LastFour,
        ExpiryMonth = method.ExpiryMonth,
        ExpiryYear = method.ExpiryYear,
        FundingSource = method.FundingSource,
        IssuerCountry = method.IssuerCountry,
        Status = method.Status.ToString().ToUpperInvariant()
    };
    private static StoredPaymentMethodOperationResult Success() => new(true, null, PaymentFailureKind.None, string.Empty, string.Empty);
    private static StoredPaymentMethodOperationResult Unavailable() => new(false, null, PaymentFailureKind.Unavailable, "payment_provider_unavailable", "The payment provider is temporarily unavailable.");
    private static StoredPaymentMethodOperationResult Failed(PaymentOperationResult result) => new(false, null, result.FailureKind, result.ErrorCode, result.ErrorMessage);
    private static DateTime NextAuthoritativeTimestamp(StoredPaymentMethod method)
    {
        var now = DateTime.UtcNow;
        return method.LastProviderEventAtUtc >= now ? method.LastProviderEventAtUtc.AddTicks(1) : now;
    }
}
