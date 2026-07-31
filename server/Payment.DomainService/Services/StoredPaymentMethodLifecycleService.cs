using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class StoredPaymentMethodLifecycleService :
    IStoredPaymentMethodLifecycleService
{
    private readonly IStoredPaymentMethodRepository _methods;
    private readonly IPaymentRepository _payments;
    private readonly IProviderTokenProtector _tokenProtector;
    private readonly IStoredPaymentMethodDetailProviderGatewayResolver _details;
    private readonly IStoredPaymentMethodProviderGatewayResolver _removals;
    private readonly IPaymentProviderCache _providers;
    private readonly ILogger<
        StoredPaymentMethodLifecycleService> _logger;

    public StoredPaymentMethodLifecycleService(
        IStoredPaymentMethodRepository methods,
        IPaymentRepository payments,
        IProviderTokenProtector tokenProtector,
        IStoredPaymentMethodDetailProviderGatewayResolver details,
        IStoredPaymentMethodProviderGatewayResolver removals,
        IPaymentProviderCache providers,
        ILogger<StoredPaymentMethodLifecycleService> logger)
    {
        _methods = methods;
        _payments = payments;
        _tokenProtector = tokenProtector;
        _details = details;
        _removals = removals;
        _providers = providers;
        _logger = logger;
    }

    /// <summary>
    /// Fills in card details the event did not carry. Best effort by design: a provider that
    /// reports them on the event needs no lookup, and a lookup that fails must still leave the
    /// card stored, because losing the brand is cosmetic and losing the card is not.
    /// </summary>
    private async Task<StoredPaymentMethod> WithProviderDetailsAsync(
        StoredPaymentMethod method,
        string? providerToken,
        CancellationToken cancellationToken)
    {
        // Deliberately not skipped when the event already described the card: the lookup is
        // also where the card fingerprint comes from, and without it the same card saved again
        // cannot be recognised. Only providers that mint a token per save register a gateway,
        // so the rest still make no call.
        if (string.IsNullOrWhiteSpace(providerToken))
        {
            return method;
        }

        var gateway = _details.Resolve(method.ProviderName);

        if (gateway == null)
        {
            return method;
        }

        var provider = await _providers.GetAsync(
            method.TenantId,
            method.OrganizationId,
            method.ProviderName,
            () => _payments.GetProviderAsync(
                method.TenantId,
                method.OrganizationId,
                method.ProviderName,
                cancellationToken));

        if (provider == null)
        {
            return method;
        }

        var detail = await gateway.GetAsync(provider, providerToken, cancellationToken);

        if (detail == null)
        {
            return method;
        }

        method.ProviderCardFingerprint = detail.CardFingerprint;
        method.Type = detail.Type ?? method.Type;
        method.Brand = detail.Brand;
        method.LastFour = detail.LastFour;
        method.ExpiryMonth = detail.ExpiryMonth;
        method.ExpiryYear = detail.ExpiryYear;
        method.FundingSource = detail.FundingSource;
        method.IssuerCountry = detail.IssuerCountry;

        return method;
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

        var protectedMethod = CreateProtectedMethod(webhook, payment.OrganizationId);
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

        var described = await WithProviderDetailsAsync(
            protectedMethod,
            payload.StoredPaymentMethodToken,
            cancellationToken);

        if (await TrySupersedeSameCardAsync(described, webhook.EventDateUtc, cancellationToken))
        {
            return;
        }

        await _methods.UpsertFromProviderAsync(
            described,
            webhook.EventDateUtc,
            cancellationToken);
    }

    /// <summary>
    /// Moves the card record the shopper already has onto the newly issued token, when this is
    /// the same card saved again.
    /// </summary>
    /// <remarks>
    /// Stripe mints a fresh payment method on every checkout, so saving a card the shopper
    /// already holds yields a different token and would otherwise be stored as a second card.
    /// The card fingerprint is stable across those tokens and is what tells them apart.
    /// Providers whose token is already stable per card report no fingerprint and never take
    /// this path.
    /// </remarks>
    private async Task<bool> TrySupersedeSameCardAsync(
        StoredPaymentMethod method,
        DateTime eventDateUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(method.ProviderCardFingerprint))
        {
            return false;
        }

        var existing = await _methods.GetByCardFingerprintAsync(
            method.TenantId,
            method.OrganizationId,
            method.ShopperReference,
            method.ProviderName,
            method.ProviderCardFingerprint,
            cancellationToken);

        if (existing == null ||
            string.Equals(
                existing.ProviderTokenFingerprint,
                method.ProviderTokenFingerprint,
                StringComparison.Ordinal))
        {
            // Either a card not seen before, or the same token again, which the ordinary
            // upsert already handles.
            return false;
        }

        method.ItemId = existing.ItemId;

        if (!await _methods.SupersedeTokenAsync(method, eventDateUtc, cancellationToken))
        {
            return false;
        }

        await TryDetachSupersededAsync(existing, cancellationToken);

        _logger.LogInformation(
            "Stored payment method moved onto a newly issued token TenantHash={TenantHash} PaymentMethodHash={PaymentMethodHash}",
            PaymentLogValue.Hash(method.TenantId),
            PaymentLogValue.Hash(existing.ItemId));

        return true;
    }

    /// <summary>
    /// Releases the token the card was previously held under, so the provider stops offering
    /// the same card twice. Best effort: the record already points at the new token, and a
    /// stranded token at the provider is untidy rather than harmful.
    /// </summary>
    private async Task TryDetachSupersededAsync(
        StoredPaymentMethod superseded,
        CancellationToken cancellationToken)
    {
        var gateway = _removals.Resolve(superseded.ProviderName);

        if (gateway == null ||
            !_tokenProtector.TryUnprotect(superseded, out var providerToken))
        {
            return;
        }

        var provider = await _providers.GetAsync(
            superseded.TenantId,
            superseded.OrganizationId,
            superseded.ProviderName,
            () => _payments.GetProviderAsync(
                superseded.TenantId,
                superseded.OrganizationId,
                superseded.ProviderName,
                cancellationToken));

        if (provider == null)
        {
            return;
        }

        var outcome = await gateway.RemoveAsync(
            provider,
            superseded,
            providerToken,
            cancellationToken);

        _logger.LogInformation(
            "Superseded stored payment method token release attempted Outcome={Outcome} PaymentMethodHash={PaymentMethodHash}",
            outcome,
            PaymentLogValue.Hash(superseded.ItemId));
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

        // The organization whose merchant account holds the card: from the record already held
        // when there is one, otherwise from the payment that created it.
        var organizationId = existing?.OrganizationId;

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

            organizationId = payment.OrganizationId;
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
            CreateProtectedMethod(webhook, organizationId),
            webhook.EventDateUtc,
            cancellationToken);
    }

    private StoredPaymentMethod CreateProtectedMethod(
        PaymentWebhookInbox webhook,
        string? organizationId)
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
            OrganizationId = organizationId,
            ShopperReference = payload.ShopperReference!,
            ProviderPayerReference = payload.ProviderPayerReference,
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
