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

        if (string.IsNullOrWhiteSpace(payload.CaptureId) ||
            string.IsNullOrWhiteSpace(payload.PaymentDetailId) ||
            string.IsNullOrWhiteSpace(payload.PspReference) ||
            string.IsNullOrWhiteSpace(payload.OriginalPspReference) ||
            !payload.Success.HasValue)
        {
            throw new InvalidOperationException(
                "Incomplete normalized capture event.");
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
}
