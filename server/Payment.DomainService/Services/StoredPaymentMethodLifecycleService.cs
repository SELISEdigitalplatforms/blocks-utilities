using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class StoredPaymentMethodLifecycleService :
    IStoredPaymentMethodLifecycleService
{
    private readonly IStoredPaymentMethodRepository _methods;
    private readonly IPaymentRepository _payments;
    private readonly IProviderTokenProtector _tokenProtector;
    private readonly ILogger<
        StoredPaymentMethodLifecycleService> _logger;

    public StoredPaymentMethodLifecycleService(
        IStoredPaymentMethodRepository methods,
        IPaymentRepository payments,
        IProviderTokenProtector tokenProtector,
        ILogger<StoredPaymentMethodLifecycleService> logger)
    {
        _methods = methods;
        _payments = payments;
        _tokenProtector = tokenProtector;
        _logger = logger;
    }

    public async Task ApplyAuthorisationTokenAsync(
        PaymentWebhookInbox webhook,
        PaymentDetail payment,
        CancellationToken cancellationToken)
    {
        var payload = webhook.NormalizedPayload;

        if (string.IsNullOrWhiteSpace(
                payload.StoredPaymentMethodToken) ||
            string.IsNullOrWhiteSpace(
                payload.ShopperReference))
        {
            return;
        }

        if (!string.Equals(
                payment.ShopperReference,
                payload.ShopperReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The authorization shopper reference did not match the payment.");
        }

        var protectedMethod = CreateProtectedMethod(webhook);
        var existing =
            await _methods.GetByTokenFingerprintAsync(
                webhook.TenantId,
                payload.ShopperReference,
                protectedMethod.ProviderName,
                protectedMethod.ProviderTokenFingerprint!,
                cancellationToken);

        if (existing == null &&
            !payment.RememberCard)
        {
            _logger.LogWarning(
                "Stored payment method creation skipped because save consent was not requested TenantHash={TenantHash} PaymentHash={PaymentHash}",
                PaymentLogValue.Hash(webhook.TenantId),
                PaymentLogValue.Hash(payment.ItemId));

            return;
        }

        if (existing is
            {
                Status: not PaymentMethodStatus.Active
            })
        {
            if (!payment.RememberCard ||
                !await _methods.ReactivateAfterFreshConsentAsync(
                    protectedMethod,
                    payment.CreatedAtUtc,
                    webhook.EventDateUtc,
                    cancellationToken))
            {
                _logger.LogWarning(
                    "Stored payment method reactivation skipped because fresh consent was not proven TenantHash={TenantHash} PaymentMethodHash={PaymentMethodHash}",
                    PaymentLogValue.Hash(webhook.TenantId),
                    PaymentLogValue.Hash(existing.ItemId));
            }

            return;
        }

        await _methods.UpsertFromProviderAsync(
            protectedMethod,
            webhook.EventDateUtc,
            cancellationToken);
    }

    public async Task ApplyTokenEventAsync(
        PaymentWebhookInbox webhook,
        CancellationToken cancellationToken)
    {
        var payload = webhook.NormalizedPayload;

        if (string.IsNullOrWhiteSpace(
                payload.StoredPaymentMethodToken) ||
            string.IsNullOrWhiteSpace(
                payload.ShopperReference))
        {
            throw new InvalidOperationException(
                "Incomplete normalized stored payment method event.");
        }

        var fingerprint = _tokenProtector.CreateFingerprint(
            payload.StoredPaymentMethodToken);
        var providerName =
            payload.ProviderName ??
            PaymentConstants.AdyenOnlineProvider;

        if (webhook.EventCode.Equals(
                "recurring.token.disabled",
                StringComparison.OrdinalIgnoreCase))
        {
            await _methods.MarkRemovedFromProviderAsync(
                webhook.TenantId,
                payload.ShopperReference,
                fingerprint,
                webhook.EventDateUtc,
                cancellationToken);

            return;
        }

        var existing =
            await _methods.GetByTokenFingerprintAsync(
                webhook.TenantId,
                payload.ShopperReference,
                providerName,
                fingerprint,
                cancellationToken);

        if (existing == null)
        {
            var payment = string.IsNullOrWhiteSpace(
                    payload.EventId)
                ? null
                : await _payments.GetByPspReferenceAsync(
                    webhook.TenantId,
                    payload.EventId,
                    cancellationToken);

            if (payment == null ||
                payment.PaymentStatus !=
                PaymentStatuses.Authorized ||
                !payment.RememberCard ||
                !string.Equals(
                    payment.ShopperReference,
                    payload.ShopperReference,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The token event is waiting for a correlated authorized payment.");
            }
        }
        else if (existing.Status !=
                 PaymentMethodStatus.Active)
        {
            _logger.LogWarning(
                "Stored payment method activation skipped because local removal is authoritative TenantHash={TenantHash} PaymentMethodHash={PaymentMethodHash} Status={Status}",
                PaymentLogValue.Hash(webhook.TenantId),
                PaymentLogValue.Hash(existing.ItemId),
                existing.Status);

            return;
        }

        await _methods.UpsertFromProviderAsync(
            CreateProtectedMethod(webhook),
            webhook.EventDateUtc,
            cancellationToken);
    }

    private StoredPaymentMethod CreateProtectedMethod(
        PaymentWebhookInbox webhook)
    {
        var payload = webhook.NormalizedPayload;

        if (!_tokenProtector.TryProtect(
                payload.StoredPaymentMethodToken!,
                out var protectedToken))
        {
            throw new InvalidOperationException(
                "Provider token protection is not configured.");
        }

        return new StoredPaymentMethod
        {
            TenantId = webhook.TenantId,
            ShopperReference = payload.ShopperReference!,
            ProviderName =
                payload.ProviderName ??
                PaymentConstants.AdyenOnlineProvider,
            ProviderTokenCiphertext =
                protectedToken.Ciphertext,
            ProviderTokenFingerprint =
                protectedToken.Fingerprint,
            TokenEncryptionKeyId =
                protectedToken.EncryptionKeyId,
            Type = payload.PaymentMethodType ?? "scheme",
            Brand = payload.Brand,
            LastFour = payload.LastFour,
            ExpiryMonth = payload.ExpiryMonth,
            ExpiryYear = payload.ExpiryYear,
            FundingSource = payload.FundingSource,
            IssuerCountry = payload.IssuerCountry,
            Status = PaymentMethodStatus.Active,
            LastProviderEventAtUtc =
                webhook.EventDateUtc
        };
    }
}
