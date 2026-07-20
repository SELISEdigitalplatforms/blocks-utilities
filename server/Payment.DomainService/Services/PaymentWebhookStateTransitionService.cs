using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentWebhookStateTransitionService : IPaymentWebhookStateTransitionService
{
    private readonly IPaymentRepository _payments;
    private readonly IStoredPaymentMethodLifecycleService
        _storedPaymentMethods;
    private readonly IPaymentOutboxEventFactory _events;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentRefundWebhookStateTransitionService
        _refundTransitions;
    private readonly ILogger<PaymentWebhookStateTransitionService> _logger;

    public PaymentWebhookStateTransitionService(
        IPaymentRepository payments,
        IStoredPaymentMethodLifecycleService storedPaymentMethods,
        IPaymentOutboxEventFactory events,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentRefundWebhookStateTransitionService
            refundTransitions,
        ILogger<PaymentWebhookStateTransitionService> logger)
    {
        _payments = payments;
        _storedPaymentMethods = storedPaymentMethods;
        _events = events;
        _minorUnits = minorUnits;
        _refundTransitions = refundTransitions;
        _logger = logger;
    }

    public async Task ApplyAsync(PaymentWebhookInbox webhook, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantHash"] = PaymentLogValue.Hash(webhook.TenantId),
            ["WebhookIdHash"] = PaymentLogValue.Hash(webhook.WebhookId),
            ["WebhookType"] = PaymentLogValue.Label(webhook.WebhookType),
            ["EventCode"] = PaymentLogValue.Label(webhook.EventCode),
            ["PaymentDetailIdHash"] = PaymentLogValue.Hash(
                webhook.NormalizedPayload.PaymentDetailId),
            ["ProviderEventIdHash"] = PaymentLogValue.Hash(
                webhook.PspReference ?? webhook.NormalizedPayload.EventId)
        });

        _logger.LogInformation(
            "Webhook state transition evaluation started EventDateUtc={EventDateUtc}",
            webhook.EventDateUtc);

        if (webhook.WebhookType == "token")
        {
            _logger.LogInformation(
                "Webhook state transition selected Flow=stored_payment_method");

            await _storedPaymentMethods.ApplyTokenEventAsync(
                webhook,
                cancellationToken);

            _logger.LogInformation(
                "Webhook state transition completed Flow=stored_payment_method");

            return;
        }

        if (IsRefundEvent(webhook.EventCode))
        {
            _logger.LogInformation(
                "Webhook state transition selected Flow=payment_refund");

            await _refundTransitions.ApplyAsync(
                webhook,
                cancellationToken);

            return;
        }

        if (!string.Equals(webhook.EventCode, "AUTHORISATION", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Webhook state transition skipped Reason=unsupported_standard_event");

            return;
        }

        var payload = webhook.NormalizedPayload;
        if (string.IsNullOrWhiteSpace(payload.PaymentDetailId) ||
            string.IsNullOrWhiteSpace(payload.PspReference) ||
            !payload.Success.HasValue)
        {
            _logger.LogError(
                "Webhook state transition rejected Reason=incomplete_normalized_authorisation HasPaymentId={HasPaymentId} HasPspReference={HasPspReference} HasSuccess={HasSuccess}",
                !string.IsNullOrWhiteSpace(payload.PaymentDetailId),
                !string.IsNullOrWhiteSpace(payload.PspReference),
                payload.Success.HasValue);

            throw new InvalidOperationException("Incomplete normalized authorisation event.");
        }

        _logger.LogInformation(
            "Webhook state transition loading payment");

        var payment = await _payments.GetByIdAsync(
            webhook.TenantId,
            payload.PaymentDetailId,
            cancellationToken);

        if (payment == null)
        {
            _logger.LogError(
                "Webhook state transition rejected Reason=payment_not_found");

            throw new InvalidOperationException("Payment reference was not found.");
        }

        _logger.LogInformation(
            "Webhook state transition payment loaded CurrentPaymentStatus={CurrentPaymentStatus} Currency={Currency}",
            PaymentLogValue.Label(payment.PaymentStatus),
            PaymentLogValue.Label(payment.CurrencyCode));

        if (!_minorUnits.TryConvert(
                payment.PreciseAmount,
                payment.CurrencyCode,
                out var expectedAmount))
        {
            _logger.LogError(
                "Webhook state transition rejected Reason=currency_minor_unit_conversion_failed Currency={Currency}",
                PaymentLogValue.Label(payment.CurrencyCode));

            throw new InvalidOperationException("Payment amount could not be converted to minor units.");
        }

        if (payload.AmountMinorUnits != expectedAmount ||
            !string.Equals(
                payload.CurrencyCode,
                payment.CurrencyCode,
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Webhook state transition rejected Reason=amount_or_currency_mismatch ExpectedAmountMinor={ExpectedAmountMinor} ActualAmountMinor={ActualAmountMinor} ExpectedCurrency={ExpectedCurrency} ActualCurrency={ActualCurrency}",
                expectedAmount,
                payload.AmountMinorUnits,
                PaymentLogValue.Label(payment.CurrencyCode),
                PaymentLogValue.Label(payload.CurrencyCode));

            throw new InvalidOperationException("Authorisation amount did not match the payment.");
        }

        _logger.LogInformation(
            "Webhook authorisation amount and currency validated AmountMinor={AmountMinor} Currency={Currency}",
            expectedAmount,
            PaymentLogValue.Label(payment.CurrencyCode));

        var status = payload.Success.Value ? PaymentStatuses.Authorized : PaymentStatuses.Refused;
        var eventType = payload.Success.Value ? PaymentConstants.PaymentAuthorized : PaymentConstants.PaymentRefused;
        var instrument = ToInstrument(payload);
        var outbox = _events.Create(payment, eventType, status);
        outbox.DeduplicationKey = $"{payment.ItemId}:{eventType}:{payload.PspReference}";

        _logger.LogInformation(
            "Webhook atomic payment transition started TargetPaymentStatus={TargetPaymentStatus} OutboxEventType={OutboxEventType} OutboxEventIdHash={OutboxEventIdHash}",
            status,
            eventType,
            PaymentLogValue.Hash(outbox.EventId));

        var transitionApplied = await _payments.ApplyAuthorisationAsync(
            webhook.TenantId,
            payment.ItemId,
            payload.Success.Value,
            payload.PspReference,
            webhook.EventDateUtc,
            instrument,
            outbox,
            cancellationToken);

        _logger.LogInformation(
            "Webhook atomic payment transition completed Applied={Applied} TargetPaymentStatus={TargetPaymentStatus} ReasonWhenNotApplied=duplicate_or_stale_event",
            transitionApplied,
            status);

        _logger.LogInformation(
            "Webhook stored payment method synchronization started Source=authorisation");

        await _storedPaymentMethods.ApplyAuthorisationTokenAsync(
            webhook,
            payment,
            cancellationToken);

        _logger.LogInformation(
            "Webhook stored payment method synchronization completed Source=authorisation");
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

    private static bool IsRefundEvent(string eventCode) =>
        eventCode.Equals(
            "REFUND",
            StringComparison.OrdinalIgnoreCase) ||
        eventCode.Equals(
            "REFUND_FAILED",
            StringComparison.OrdinalIgnoreCase) ||
        eventCode.Equals(
            "REFUNDED_REVERSED",
            StringComparison.OrdinalIgnoreCase);
}
