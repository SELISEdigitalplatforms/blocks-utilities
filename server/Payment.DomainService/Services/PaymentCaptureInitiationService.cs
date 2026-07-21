using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentCaptureInitiationService :
    IPaymentCaptureInitiationService
{
    private readonly IPaymentCaptureProviderGatewayResolver _gateways;
    private readonly IPaymentCaptureRequestFactory _requests;
    private readonly IPaymentCaptureRepository _captures;
    private readonly IPaymentCaptureOutboxEventFactory _events;
    private readonly IPaymentCaptureResponseMapper _responses;
    private readonly IPaymentWorkDispatcher _workDispatcher;

    public PaymentCaptureInitiationService(
        IPaymentCaptureProviderGatewayResolver gateways,
        IPaymentCaptureRequestFactory requests,
        IPaymentCaptureRepository captures,
        IPaymentCaptureOutboxEventFactory events,
        IPaymentCaptureResponseMapper responses,
        IPaymentWorkDispatcher workDispatcher)
    {
        _gateways = gateways;
        _requests = requests;
        _captures = captures;
        _events = events;
        _responses = responses;
        _workDispatcher = workDispatcher;
    }

    public async Task<PaymentCaptureOperationResult> SubmitAsync(
        PaymentDetail payment,
        PaymentCapture capture,
        PaymentProvider provider,
        string leaseId,
        long minorUnits,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var gateway = _gateways.Resolve(capture.ProviderName);

        if (gateway == null)
        {
            return await MarkUnknownAsync(
                payment,
                capture,
                leaseId,
                PaymentCaptureProviderOutcome.Unavailable,
                correlationId,
                cancellationToken);
        }

        var providerResult = await gateway.SubmitAsync(
            provider,
            capture.OriginalPaymentPspReference,
            _requests.Create(capture, minorUnits),
            capture.IdempotencyKey,
            cancellationToken);

        if (providerResult.Outcome ==
                PaymentCaptureProviderOutcome.Submitted &&
            !string.IsNullOrWhiteSpace(
                providerResult.ProviderCaptureReference))
        {
            var outbox = _events.Create(
                payment,
                capture,
                PaymentConstants.PaymentCaptureRequested,
                PaymentCaptureStatuses.Submitted);
            var updated = await _captures.CompleteSubmissionAsync(
                payment.TenantId,
                payment.ItemId,
                capture.CaptureId,
                leaseId,
                providerResult.ProviderCaptureReference,
                providerResult.ProviderStatus,
                outbox,
                cancellationToken);

            if (!updated)
            {
                return Conflict(correlationId);
            }

            await _workDispatcher.TryDispatchAsync(
                payment.TenantId,
                includeRecovery: false,
                cancellationToken: cancellationToken);

            capture.Status = PaymentCaptureStatuses.Submitted;
            capture.ProviderCaptureReference =
                providerResult.ProviderCaptureReference;
            capture.ProviderResultStatus = providerResult.ProviderStatus;
            capture.SubmittedAtUtc = DateTime.UtcNow;

            return PaymentCaptureOperationResult.Success(
                _responses.Map(payment.ItemId, capture),
                correlationId);
        }

        if (providerResult.Outcome ==
            PaymentCaptureProviderOutcome.Rejected)
        {
            var failureCode = providerResult.SafeErrorCode ??
                              "payment_capture_provider_rejected";
            var outbox = _events.Create(
                payment,
                capture,
                PaymentConstants.PaymentCaptureFailed,
                PaymentCaptureStatuses.Failed);
            var updated = await _captures.CompleteRejectionAsync(
                payment.TenantId,
                payment.ItemId,
                capture.CaptureId,
                leaseId,
                capture.Amount,
                failureCode,
                outbox,
                cancellationToken);

            if (!updated)
            {
                return Conflict(correlationId);
            }

            await _workDispatcher.TryDispatchAsync(
                payment.TenantId,
                includeRecovery: false,
                cancellationToken: cancellationToken);

            return PaymentCaptureOperationResult.Failure(
                PaymentFailureKind.ProviderRejected,
                failureCode,
                "The payment provider rejected the capture.",
                correlationId);
        }

        return await MarkUnknownAsync(
            payment,
            capture,
            leaseId,
            providerResult.Outcome,
            correlationId,
            cancellationToken);
    }

    private async Task<PaymentCaptureOperationResult> MarkUnknownAsync(
        PaymentDetail payment,
        PaymentCapture capture,
        string leaseId,
        PaymentCaptureProviderOutcome outcome,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var unavailable = outcome ==
                          PaymentCaptureProviderOutcome.Unavailable;
        var failureCode = unavailable
            ? "payment_provider_unavailable"
            : "payment_capture_initiation_unknown";

        await _captures.MarkInitiationUnknownAsync(
            payment.TenantId,
            payment.ItemId,
            capture.CaptureId,
            leaseId,
            failureCode,
            DateTime.UtcNow.AddSeconds(30),
            cancellationToken);

        await _workDispatcher.TryDispatchAsync(
            payment.TenantId,
            includeRecovery: true,
            scheduledAtUtc: DateTimeOffset.UtcNow.AddSeconds(30),
            cancellationToken: cancellationToken);

        return PaymentCaptureOperationResult.Failure(
            unavailable
                ? PaymentFailureKind.Unavailable
                : outcome == PaymentCaptureProviderOutcome.Timeout
                    ? PaymentFailureKind.Timeout
                    : PaymentFailureKind.ProviderFailure,
            failureCode,
            unavailable
                ? "The payment provider is temporarily unavailable."
                : "The provider outcome is unknown. Retry with the same idempotency key.",
            correlationId);
    }

    private static PaymentCaptureOperationResult Conflict(
        string correlationId) =>
        PaymentCaptureOperationResult.Failure(
            PaymentFailureKind.Conflict,
            "payment_capture_state_conflict",
            "The capture state changed while processing.",
            correlationId);
}
