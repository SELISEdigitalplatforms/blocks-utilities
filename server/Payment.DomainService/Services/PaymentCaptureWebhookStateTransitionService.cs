using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentCaptureWebhookStateTransitionService :
    IPaymentCaptureWebhookStateTransitionService
{
    private readonly IPaymentCaptureRepository _captures;
    private readonly ICurrencyMinorUnitResolver _minorUnits;
    private readonly IPaymentCaptureOutboxEventFactory _events;
    private readonly ILogger<
        PaymentCaptureWebhookStateTransitionService> _logger;

    public PaymentCaptureWebhookStateTransitionService(
        IPaymentCaptureRepository captures,
        ICurrencyMinorUnitResolver minorUnits,
        IPaymentCaptureOutboxEventFactory events,
        ILogger<PaymentCaptureWebhookStateTransitionService> logger)
    {
        _captures = captures;
        _minorUnits = minorUnits;
        _events = events;
        _logger = logger;
    }

    public async Task ApplyAsync(
        PaymentWebhookInbox webhook,
        CancellationToken cancellationToken)
    {
        var payload = webhook.NormalizedPayload;

        if (string.IsNullOrWhiteSpace(payload.PaymentDetailId) ||
            string.IsNullOrWhiteSpace(payload.PspReference) ||
            string.IsNullOrWhiteSpace(payload.OriginalPspReference) ||
            !payload.Success.HasValue)
        {
            throw new InvalidOperationException(
                "Incomplete normalized capture event.");
        }

        // A capture made in the provider's own dashboard has no capture record here to settle,
        // because this service never requested it. The money still moved, so it is applied to
        // the payment instead. Demanding a capture id threw and dead-lettered the event, which
        // left the payment showing as merely authorised.
        if (string.IsNullOrWhiteSpace(payload.CaptureId))
        {
            await ApplyExternalCaptureAsync(webhook, cancellationToken);

            return;
        }

        var payment = await _captures.GetPaymentByCaptureIdAsync(
            webhook.TenantId,
            payload.CaptureId,
            cancellationToken);
        var capture = payment?.Captures.FirstOrDefault(
            candidate => candidate.CaptureId == payload.CaptureId);

        if (payment == null ||
            capture == null ||
            payment.ItemId != payload.PaymentDetailId)
        {
            throw new InvalidOperationException(
                "The capture reference was not found.");
        }

        if (!string.Equals(
                capture.OriginalPaymentPspReference,
                payload.OriginalPspReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The original payment reference did not match.");
        }

        if (!_minorUnits.TryConvert(
                capture.Amount,
                capture.CurrencyCode,
                out var expectedMinorUnits) ||
            payload.AmountMinorUnits != expectedMinorUnits ||
            !string.Equals(
                payload.CurrencyCode,
                capture.CurrencyCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The capture amount did not match.");
        }

        var succeeded = webhook.EventCode.Equals(
                            "CAPTURE",
                            StringComparison.OrdinalIgnoreCase) &&
                        payload.Success.Value;
        var targetCaptureStatus = succeeded
            ? PaymentCaptureStatuses.Succeeded
            : PaymentCaptureStatuses.Failed;
        var targetPaymentStatus = succeeded
            ? payment.CapturedAmount + capture.Amount >=
              payment.AuthorizedAmount
                ? PaymentStatuses.Captured
                : PaymentStatuses.PartiallyCaptured
            : payment.PaymentStatus;
        var eventType = succeeded
            ? PaymentConstants.PaymentCaptured
            : PaymentConstants.PaymentCaptureFailed;
        var failureCode = succeeded
            ? null
            : payload.ProviderFailureCode ??
              "payment_capture_provider_failed";
        var outbox = _events.Create(
            payment,
            capture,
            eventType,
            targetCaptureStatus);
        outbox.DeduplicationKey =
            $"{capture.CaptureId}:{eventType}:{payload.PspReference}";

        var applied = await _captures.ApplyProviderEventAsync(
            webhook.TenantId,
            payment.ItemId,
            capture.CaptureId,
            [
                PaymentCaptureStatuses.Initiating,
                PaymentCaptureStatuses.InitiationUnknown,
                PaymentCaptureStatuses.Submitted
            ],
            targetCaptureStatus,
            targetPaymentStatus,
            payload.PspReference,
            webhook.EventDateUtc,
            -capture.Amount,
            succeeded ? capture.Amount : 0,
            failureCode,
            outbox,
            cancellationToken);

        _logger.LogInformation(
            "Payment capture webhook transition completed EventCode={EventCode} TargetStatus={TargetStatus} Applied={Applied} PaymentHash={PaymentHash} CaptureHash={CaptureHash} FailureCode={FailureCode}",
            PaymentLogValue.Label(webhook.EventCode),
            targetCaptureStatus,
            applied,
            PaymentLogValue.Hash(payment.ItemId),
            PaymentLogValue.Hash(capture.CaptureId),
            PaymentLogValue.Label(failureCode));
    }

    /// <summary>
    /// Applies a capture this service never requested to the payment alone.
    /// </summary>
    private async Task ApplyExternalCaptureAsync(
        PaymentWebhookInbox webhook,
        CancellationToken cancellationToken)
    {
        var payload = webhook.NormalizedPayload;
        var payment = await _captures.GetPaymentAsync(
            webhook.TenantId,
            payload.PaymentDetailId!,
            cancellationToken);

        if (payment == null)
        {
            throw new InvalidOperationException("The payment was not found.");
        }

        if (!string.Equals(
                payment.PspReference,
                payload.OriginalPspReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The original payment reference did not match.");
        }

        if (!payload.Success.Value)
        {
            // Nothing was captured, and there is no capture record to fail.
            _logger.LogInformation(
                "External capture reported as failed PaymentHash={PaymentHash}",
                PaymentLogValue.Hash(payment.ItemId));

            return;
        }

        if (!payload.AmountMinorUnits.HasValue ||
            !string.Equals(
                payload.CurrencyCode,
                payment.CurrencyCode,
                StringComparison.OrdinalIgnoreCase) ||
            !_minorUnits.TryConvertBack(
                payload.AmountMinorUnits.Value,
                payment.CurrencyCode,
                out var amount))
        {
            throw new InvalidOperationException(
                "The external capture amount could not be read.");
        }

        var targetPaymentStatus =
            payment.CapturedAmount + amount >= payment.AuthorizedAmount
                ? PaymentStatuses.Captured
                : PaymentStatuses.PartiallyCaptured;
        var outbox = _events.Create(
            payment,
            // Describes the capture for the emitted event only; nothing is persisted, because
            // this service holds no record of a capture it did not make.
            new PaymentCapture
            {
                CaptureId = payload.PspReference!,
                ProviderName = payment.ProviderName,
                CorrelationId = payment.CorrelationId,
                Amount = amount,
                CurrencyCode = payment.CurrencyCode
            },
            PaymentConstants.PaymentCaptured,
            PaymentCaptureStatuses.Succeeded);
        outbox.DeduplicationKey =
            $"{payment.ItemId}:{PaymentConstants.PaymentCaptured}:{payload.PspReference}";

        var applied = await _captures.ApplyExternalCaptureAsync(
            webhook.TenantId,
            payment.ItemId,
            targetPaymentStatus,
            amount,
            payload.PspReference!,
            webhook.EventDateUtc,
            outbox,
            cancellationToken);

        _logger.LogInformation(
            "External capture applied to the payment Applied={Applied} TargetPaymentStatus={TargetPaymentStatus} PaymentHash={PaymentHash}",
            applied,
            targetPaymentStatus,
            PaymentLogValue.Hash(payment.ItemId));
    }
}
