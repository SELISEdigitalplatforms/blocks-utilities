using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentRefundInitiationService :
    IPaymentRefundInitiationService
{
    private readonly IPaymentRefundProviderGatewayResolver
        _gateways;
    private readonly IPaymentRefundRequestFactory
        _requestFactory;
    private readonly IPaymentRefundRepository _refunds;
    private readonly IPaymentRefundOutboxEventFactory _events;
    private readonly IPaymentRefundResponseMapper _responses;
    private readonly ILogger<PaymentRefundInitiationService>
        _logger;

    public PaymentRefundInitiationService(
        IPaymentRefundProviderGatewayResolver gateways,
        IPaymentRefundRequestFactory requestFactory,
        IPaymentRefundRepository refunds,
        IPaymentRefundOutboxEventFactory events,
        IPaymentRefundResponseMapper responses,
        ILogger<PaymentRefundInitiationService> logger)
    {
        _gateways = gateways;
        _requestFactory = requestFactory;
        _refunds = refunds;
        _events = events;
        _responses = responses;
        _logger = logger;
    }

    public async Task<PaymentRefundOperationResult> SubmitAsync(
        PaymentDetail payment,
        PaymentRefund refund,
        PaymentProvider provider,
        string leaseId,
        long minorUnits,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var gateway = _gateways.Resolve(
            refund.ProviderName);

        if (gateway == null)
        {
            return await MarkUnknownAsync(
                payment,
                refund,
                leaseId,
                PaymentRefundProviderOutcome.Unavailable,
                correlationId,
                cancellationToken);
        }

        var request = _requestFactory.Create(
            refund,
            minorUnits);
        var providerResult = await gateway.SubmitAsync(
            provider,
            refund.OriginalPaymentPspReference,
            request,
            refund.IdempotencyKey,
            cancellationToken);

        if (providerResult.Outcome ==
                PaymentRefundProviderOutcome.Submitted &&
            !string.IsNullOrWhiteSpace(
                providerResult.ProviderRefundReference))
        {
            var outbox = _events.Create(
                payment,
                refund,
                PaymentConstants.PaymentRefundRequested,
                PaymentRefundStatuses.Submitted);
            var updated =
                await _refunds.CompleteSubmissionAsync(
                    payment.TenantId,
                    payment.ItemId,
                    refund.RefundId,
                    leaseId,
                    providerResult
                        .ProviderRefundReference,
                    providerResult.ProviderStatus,
                    outbox,
                    cancellationToken);

            if (!updated)
            {
                return Conflict(correlationId);
            }

            refund.Status =
                PaymentRefundStatuses.Submitted;
            refund.ProviderRefundReference =
                providerResult.ProviderRefundReference;
            refund.ProviderResultStatus =
                providerResult.ProviderStatus;
            refund.SubmittedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Payment refund submitted TenantHash={TenantHash} PaymentHash={PaymentHash} RefundHash={RefundHash}",
                PaymentLogValue.Hash(payment.TenantId),
                PaymentLogValue.Hash(payment.ItemId),
                PaymentLogValue.Hash(refund.RefundId));

            return PaymentRefundOperationResult.Success(
                _responses.Map(
                    payment.ItemId,
                    refund),
                correlationId);
        }

        if (providerResult.Outcome ==
            PaymentRefundProviderOutcome.Rejected)
        {
            var failureCode =
                "payment_refund_provider_rejected";
            var outbox = _events.Create(
                payment,
                refund,
                PaymentConstants.PaymentRefundFailed,
                PaymentRefundStatuses.Failed);
            var updated =
                await _refunds.CompleteRejectionAsync(
                    payment.TenantId,
                    payment.ItemId,
                    refund.RefundId,
                    leaseId,
                    refund.Amount,
                    failureCode,
                    outbox,
                    cancellationToken);

            return updated
                ? PaymentRefundOperationResult.Failure(
                    PaymentFailureKind.ProviderRejected,
                    failureCode,
                    "The payment provider rejected the refund.",
                    correlationId)
                : Conflict(correlationId);
        }

        return await MarkUnknownAsync(
            payment,
            refund,
            leaseId,
            providerResult.Outcome,
            correlationId,
            cancellationToken);
    }

    private async Task<PaymentRefundOperationResult>
        MarkUnknownAsync(
            PaymentDetail payment,
            PaymentRefund refund,
            string leaseId,
            PaymentRefundProviderOutcome outcome,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var unavailable =
            outcome ==
            PaymentRefundProviderOutcome.Unavailable;
        var failureCode = unavailable
            ? "payment_provider_unavailable"
            : "payment_refund_initiation_unknown";

        await _refunds.MarkInitiationUnknownAsync(
            payment.TenantId,
            payment.ItemId,
            refund.RefundId,
            leaseId,
            failureCode,
            DateTime.UtcNow.AddSeconds(30),
            cancellationToken);

        return PaymentRefundOperationResult.Failure(
            unavailable
                ? PaymentFailureKind.Unavailable
                : outcome ==
                  PaymentRefundProviderOutcome.Timeout
                    ? PaymentFailureKind.Timeout
                    : PaymentFailureKind.ProviderFailure,
            failureCode,
            unavailable
                ? "The payment provider is temporarily unavailable."
                : "The provider outcome is unknown. Retry with the same idempotency key.",
            correlationId);
    }

    private static PaymentRefundOperationResult Conflict(
        string correlationId) =>
        PaymentRefundOperationResult.Failure(
            PaymentFailureKind.Conflict,
            "payment_refund_state_conflict",
            "The refund state changed while processing.",
            correlationId);
}
