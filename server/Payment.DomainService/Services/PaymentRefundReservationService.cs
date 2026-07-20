using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentRefundReservationService :
    IPaymentRefundReservationService
{
    private readonly IPaymentRefundRepository _refunds;
    private readonly IPaymentRefundWebhookReferenceService
        _references;
    private readonly IPaymentRefundResponseMapper _responses;
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public PaymentRefundReservationService(
        IPaymentRefundRepository refunds,
        IPaymentRefundWebhookReferenceService references,
        IPaymentRefundResponseMapper responses,
        IOptionsMonitor<PaymentOptions> options)
    {
        _refunds = refunds;
        _references = references;
        _responses = responses;
        _options = options;
    }

    public async Task<PaymentRefundReservationResult>
        ReserveAsync(
            PaymentDetail payment,
            PaymentProvider provider,
            CreatePaymentRefundRequest request,
            string idempotencyKey,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var refundId = Guid.NewGuid().ToString();

        if (!_references.TryCreate(
                payment.TenantId,
                refundId,
                out var providerReference))
        {
            return Terminal(
                PaymentFailureKind.Unavailable,
                "payment_refund_reference_unavailable",
                "The refund could not be prepared.",
                correlationId);
        }

        var requestHash =
            PaymentHashing.CreateRefundRequestHash(
                payment.ItemId,
                request);
        var leaseId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var refund = new PaymentRefund
        {
            RefundId = refundId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Status = PaymentRefundStatuses.Initiating,
            Amount = request.Amount,
            CurrencyCode = payment.CurrencyCode,
            Reason = request.Reason?.Trim(),
            ProviderName = provider.ProviderName,
            ProviderReference = providerReference,
            ProviderMerchantAccount =
                provider.MerchantId,
            OriginalPaymentPspReference =
                payment.PspReference!,
            CorrelationId = correlationId,
            ProcessingLeaseId = leaseId,
            ProcessingLeaseExpiresAtUtc =
                now.Add(
                    PaymentRefundLeasePolicy.Resolve(
                        _options.CurrentValue)),
            InitiationAttemptCount = 1,
            NextRecoveryAttemptAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (await _refunds.TryReserveAsync(
                payment.TenantId,
                payment.ItemId,
                refund,
                Math.Clamp(
                    _options.CurrentValue
                        .MaximumRefundsPerPayment,
                    1,
                    200),
                cancellationToken))
        {
            return new PaymentRefundReservationResult(
                payment,
                refund,
                leaseId,
                null);
        }

        var existingPayment =
            await _refunds
                .GetPaymentByRefundIdempotencyKeyAsync(
                    payment.TenantId,
                    idempotencyKey,
                    cancellationToken);
        var existingRefund =
            existingPayment?.Refunds.FirstOrDefault(
                candidate =>
                    candidate.IdempotencyKey ==
                    idempotencyKey);

        if (existingPayment == null ||
            existingRefund == null)
        {
            return Terminal(
                PaymentFailureKind.Conflict,
                "payment_refund_not_available",
                "The requested amount is not available for refund.",
                correlationId);
        }

        if (!PaymentHashing.RequestHashesMatch(
                existingRefund.RequestHash,
                requestHash))
        {
            return Terminal(
                PaymentFailureKind.Conflict,
                "idempotency_key_reused",
                "The idempotency key was already used with a different refund request.",
                correlationId);
        }

        if (existingRefund.Status is
            PaymentRefundStatuses.Submitted or
            PaymentRefundStatuses.Succeeded or
            PaymentRefundStatuses.Reversed)
        {
            return new PaymentRefundReservationResult(
                null,
                null,
                null,
                PaymentRefundOperationResult.Success(
                    _responses.Map(
                        existingPayment.ItemId,
                        existingRefund),
                    correlationId,
                    replay: true));
        }

        if (existingRefund.Status ==
            PaymentRefundStatuses.Failed)
        {
            return Terminal(
                PaymentFailureKind.ProviderRejected,
                existingRefund.FailureCode ??
                "payment_refund_failed",
                "The previous refund attempt failed.",
                correlationId);
        }

        var recoveryLeaseId =
            Guid.NewGuid().ToString("N");
        var claimed =
            await _refunds.TryClaimInitiationAsync(
                payment.TenantId,
                existingPayment.ItemId,
                existingRefund.RefundId,
                recoveryLeaseId,
                DateTime.UtcNow.Add(
                    PaymentRefundLeasePolicy.Resolve(
                        _options.CurrentValue)),
                cancellationToken);

        return claimed == null
            ? Terminal(
                PaymentFailureKind.Conflict,
                "payment_refund_in_progress",
                "The refund is already being processed.",
                correlationId)
            : new PaymentRefundReservationResult(
                existingPayment,
                claimed,
                recoveryLeaseId,
                null);
    }

    private static PaymentRefundReservationResult Terminal(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        new(
            null,
            null,
            null,
            PaymentRefundOperationResult.Failure(
                failureKind,
                errorCode,
                errorMessage,
                correlationId));
}
