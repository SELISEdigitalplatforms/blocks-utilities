using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public interface IPaymentRefundRepository
{
    Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken);

    Task<PaymentDetail?> GetPaymentAsync(
        string tenantId,
        string paymentDetailId,
        CancellationToken cancellationToken);

    Task<PaymentDetail?> GetPaymentByRefundIdAsync(
        string tenantId,
        string refundId,
        CancellationToken cancellationToken);

    Task<PaymentDetail?> GetPaymentByRefundIdempotencyKeyAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<bool> TryReserveAsync(
        string tenantId,
        string paymentDetailId,
        PaymentRefund refund,
        int maximumRefunds,
        CancellationToken cancellationToken);

    Task<PaymentRefund?> TryClaimInitiationAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task<bool> CompleteSubmissionAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string leaseId,
        string providerRefundReference,
        string? providerStatus,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);

    Task<bool> CompleteRejectionAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string leaseId,
        decimal amount,
        string failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);

    Task MarkInitiationUnknownAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string leaseId,
        string failureCode,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken);

    Task MarkRequiresAttentionAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string? leaseId,
        string failureCode,
        CancellationToken cancellationToken);

    Task<bool> ApplyProviderEventAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        IReadOnlyCollection<string> expectedStatuses,
        string targetStatus,
        string providerRefundReference,
        DateTime eventDateUtc,
        decimal reservedAmountDelta,
        decimal refundedAmountDelta,
        string targetPaymentStatus,
        string? completionAction,
        string? failureCode,
        string? failureSummary,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);

    Task<List<PaymentDetail>>
        GetPaymentsWithDueRefundInitiationsAsync(
            string tenantId,
            DateTime utcNow,
            int limit,
            CancellationToken cancellationToken);

    Task<List<PaymentDetail>>
        GetPaymentsWithDueRefundOutboxEventsAsync(
            string tenantId,
            DateTime utcNow,
            int limit,
            CancellationToken cancellationToken);

    Task<bool> TryClaimOutboxEventAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string eventId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task MarkOutboxPublishedAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string eventId,
        string leaseId,
        DateTime publishedAtUtc,
        CancellationToken cancellationToken);

    Task MarkOutboxFailedAsync(
        string tenantId,
        string paymentDetailId,
        string refundId,
        string eventId,
        string leaseId,
        PaymentOutboxStatus status,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string safeError,
        CancellationToken cancellationToken);
}
