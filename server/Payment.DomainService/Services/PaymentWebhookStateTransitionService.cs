using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentWebhookStateTransitionService : IPaymentWebhookStateTransitionService
{
    private readonly IPaymentRepository _payments;
    private readonly IStoredPaymentMethodRepository _methods;
    private readonly IPaymentOutboxEventFactory _events;
    private readonly ICurrencyMinorUnitResolver _minorUnits;

    public PaymentWebhookStateTransitionService(
        IPaymentRepository payments,
        IStoredPaymentMethodRepository methods,
        IPaymentOutboxEventFactory events,
        ICurrencyMinorUnitResolver minorUnits)
    {
        _payments = payments;
        _methods = methods;
        _events = events;
        _minorUnits = minorUnits;
    }

    public async Task ApplyAsync(PaymentWebhookInbox webhook, CancellationToken cancellationToken)
    {
        if (webhook.WebhookType == "token")
        {
            await ApplyTokenAsync(webhook, cancellationToken);
            return;
        }
        if (!string.Equals(webhook.EventCode, "AUTHORISATION", StringComparison.OrdinalIgnoreCase)) return;

        var payload = webhook.NormalizedPayload;
        if (string.IsNullOrWhiteSpace(payload.MerchantReference) || string.IsNullOrWhiteSpace(payload.PspReference) || !payload.Success.HasValue)
            throw new InvalidOperationException("Incomplete normalized authorisation event.");
        var payment = await _payments.GetByIdAsync(webhook.TenantId, payload.MerchantReference, cancellationToken)
            ?? throw new InvalidOperationException("Payment reference was not found.");
        if (!_minorUnits.TryConvert(payment.PreciseAmount, payment.CurrencyCode, out var expectedAmount) ||
            payload.AmountMinorUnits != expectedAmount ||
            !string.Equals(payload.CurrencyCode, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Authorisation amount did not match the payment.");

        var status = payload.Success.Value ? PaymentStatuses.Authorized : PaymentStatuses.Refused;
        var eventType = payload.Success.Value ? PaymentConstants.PaymentAuthorized : PaymentConstants.PaymentRefused;
        var instrument = ToInstrument(payload);
        var outbox = _events.Create(payment, eventType, status);
        outbox.DeduplicationKey = $"{payment.ItemId}:{eventType}:{payload.PspReference}";
        await _payments.ApplyAuthorisationAsync(
            webhook.TenantId,
            payment.ItemId,
            payload.Success.Value,
            payload.PspReference,
            webhook.EventDateUtc,
            instrument,
            outbox,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(payload.StoredPaymentMethodToken) && !string.IsNullOrWhiteSpace(payload.ShopperReference))
            await _methods.UpsertFromProviderAsync(ToStoredMethod(webhook, PaymentMethodStatus.Active), webhook.EventDateUtc, cancellationToken);
    }

    private async Task ApplyTokenAsync(PaymentWebhookInbox webhook, CancellationToken cancellationToken)
    {
        var payload = webhook.NormalizedPayload;
        if (string.IsNullOrWhiteSpace(payload.StoredPaymentMethodToken) || string.IsNullOrWhiteSpace(payload.ShopperReference))
            throw new InvalidOperationException("Incomplete normalized token event.");
        var status = webhook.EventCode.Equals("recurring.token.disabled", StringComparison.OrdinalIgnoreCase)
            ? PaymentMethodStatus.Disabled
            : PaymentMethodStatus.Active;
        await _methods.UpsertFromProviderAsync(ToStoredMethod(webhook, status), webhook.EventDateUtc, cancellationToken);
    }

    private static StoredPaymentMethod ToStoredMethod(PaymentWebhookInbox webhook, PaymentMethodStatus status)
    {
        var payload = webhook.NormalizedPayload;
        return new StoredPaymentMethod
        {
            TenantId = webhook.TenantId,
            ShopperReference = payload.ShopperReference!,
            ProviderName = payload.ProviderName ?? PaymentConstants.AdyenOnlineProvider,
            StoredPaymentMethodToken = payload.StoredPaymentMethodToken!,
            Type = payload.PaymentMethodType ?? "scheme",
            Brand = payload.Brand,
            LastFour = payload.LastFour,
            ExpiryMonth = payload.ExpiryMonth,
            ExpiryYear = payload.ExpiryYear,
            FundingSource = payload.FundingSource,
            IssuerCountry = payload.IssuerCountry,
            Status = status,
            LastProviderEventAtUtc = webhook.EventDateUtc
        };
    }

    private static PaymentInstrument ToInstrument(PaymentWebhookPayload payload) => new()
    {
        Type = payload.PaymentMethodType,
        Brand = payload.Brand,
        LastFour = payload.LastFour,
        ExpiryMonth = payload.ExpiryMonth,
        ExpiryYear = payload.ExpiryYear,
        FundingSource = payload.FundingSource,
        IssuerCountry = payload.IssuerCountry,
        IssuerName = payload.IssuerName,
        AuthorizationCode = payload.AuthorizationCode
    };
}
