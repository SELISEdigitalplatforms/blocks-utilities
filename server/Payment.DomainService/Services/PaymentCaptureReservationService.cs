using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentCaptureReservationService :
    IPaymentCaptureReservationService
{
    private readonly IPaymentCaptureRepository _captures;
    private readonly IPaymentCaptureWebhookReferenceService _references;
    private readonly IPaymentCaptureResponseMapper _responses;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly TimeProvider _time;

    public PaymentCaptureReservationService(
        IPaymentCaptureRepository captures,
        IPaymentCaptureWebhookReferenceService references,
        IPaymentCaptureResponseMapper responses,
        IOptionsMonitor<PaymentOptions> options,
        TimeProvider? time = null)
    {
        _captures = captures;
        _references = references;
        _responses = responses;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public async Task<PaymentCaptureReservationResult> ReserveAsync(
        PaymentDetail payment,
        PaymentProvider provider,
        CreatePaymentCaptureRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var captureId = Guid.NewGuid().ToString();

        if (!_references.TryCreate(
                payment.TenantId,
                captureId,
                out var providerReference))
        {
            return Terminal(
                PaymentFailureKind.Unavailable,
                "payment_capture_reference_unavailable",
                "The capture could not be prepared.",
                correlationId);
        }

        var requestHash = PaymentHashing.CreateCaptureRequestHash(
            payment.ItemId,
            request);
        var leaseId = Guid.NewGuid().ToString("N");
        var now = _time.GetUtcNow().UtcDateTime;
        var capture = new PaymentCapture
        {
            CaptureId = captureId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Status = PaymentCaptureStatuses.Initiating,
            Amount = request.Amount,
            CurrencyCode = payment.CurrencyCode,
            ProviderName = provider.ProviderName,
            ProviderReference = providerReference,
            ProviderMerchantAccount = provider.MerchantId,
            OriginalPaymentPspReference = payment.PspReference!,
            CorrelationId = correlationId,
            ProcessingLeaseId = leaseId,
            ProcessingLeaseExpiresAtUtc = now.Add(
                PaymentCaptureLeasePolicy.Resolve(
                    _options.CurrentValue)),
            InitiationAttemptCount = 1,
            NextRecoveryAttemptAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (await _captures.TryReserveAsync(
                payment.TenantId,
                payment.ItemId,
                capture,
                Math.Clamp(
                    _options.CurrentValue.MaximumCapturesPerPayment,
                    1,
                    200),
                cancellationToken))
        {
            return new PaymentCaptureReservationResult(
                payment,
                capture,
                leaseId,
                null);
        }

        var existingPayment =
            await _captures.GetPaymentByIdempotencyKeyAsync(
                payment.TenantId,
                idempotencyKey,
                cancellationToken);
        var existingCapture = existingPayment?.Captures.FirstOrDefault(
            candidate => candidate.IdempotencyKey == idempotencyKey);

        if (existingPayment == null || existingCapture == null)
        {
            return Terminal(
                PaymentFailureKind.Conflict,
                "payment_capture_not_available",
                "The requested amount is not available for capture.",
                correlationId);
        }

        if (!PaymentHashing.RequestHashesMatch(
                existingCapture.RequestHash,
                requestHash))
        {
            return Terminal(
                PaymentFailureKind.Conflict,
                "idempotency_key_reused",
                "The idempotency key was already used with a different capture request.",
                correlationId);
        }

        if (existingCapture.Status is
            PaymentCaptureStatuses.Submitted or
            PaymentCaptureStatuses.Succeeded)
        {
            return new PaymentCaptureReservationResult(
                null,
                null,
                null,
                PaymentCaptureOperationResult.Success(
                    _responses.Map(
                        existingPayment.ItemId,
                        existingCapture),
                    correlationId,
                    replay: true));
        }

        if (existingCapture.Status == PaymentCaptureStatuses.Failed)
        {
            return Terminal(
                PaymentFailureKind.ProviderRejected,
                existingCapture.FailureCode ??
                "payment_capture_failed",
                "The previous capture attempt failed.",
                correlationId);
        }

        var recoveryLeaseId = Guid.NewGuid().ToString("N");
        var claimed = await _captures.TryClaimInitiationAsync(
            payment.TenantId,
            existingPayment.ItemId,
            existingCapture.CaptureId,
            recoveryLeaseId,
            _time.GetUtcNow().UtcDateTime.Add(
                PaymentCaptureLeasePolicy.Resolve(
                    _options.CurrentValue)),
            cancellationToken);

        return claimed == null
            ? Terminal(
                PaymentFailureKind.Conflict,
                "payment_capture_in_progress",
                "The capture is already being processed.",
                correlationId)
            : new PaymentCaptureReservationResult(
                existingPayment,
                claimed,
                recoveryLeaseId,
                null);
    }

    private static PaymentCaptureReservationResult Terminal(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        new(
            null,
            null,
            null,
            PaymentCaptureOperationResult.Failure(
                failureKind,
                errorCode,
                errorMessage,
                correlationId));
}
