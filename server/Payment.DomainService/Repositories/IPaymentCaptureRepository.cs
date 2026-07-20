using Payment.DomainService.Entities;

namespace Payment.DomainService.Repositories;

public interface IPaymentCaptureRepository
{
    Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken);

    Task<PaymentDetail?> GetPaymentAsync(
        string tenantId,
        string paymentDetailId,
        CancellationToken cancellationToken);

    Task<PaymentDetail?> GetPaymentByCaptureIdAsync(
        string tenantId,
        string captureId,
        CancellationToken cancellationToken);

    Task<PaymentDetail?> GetPaymentByIdempotencyKeyAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<bool> TryReserveAsync(
        string tenantId,
        string paymentDetailId,
        PaymentCapture capture,
        int maximumCaptures,
        CancellationToken cancellationToken);

    Task<PaymentCapture?> TryClaimInitiationAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task<bool> CompleteSubmissionAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string leaseId,
        string providerCaptureReference,
        string? providerStatus,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);

    Task<bool> CompleteRejectionAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string leaseId,
        decimal amount,
        string failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);

    Task MarkInitiationUnknownAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string leaseId,
        string failureCode,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken);

    Task MarkRequiresAttentionAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string? leaseId,
        string failureCode,
        CancellationToken cancellationToken);

    Task<List<PaymentDetail>> GetPaymentsWithDueCaptureInitiationsAsync(
        string tenantId,
        DateTime utcNow,
        int limit,
        CancellationToken cancellationToken);

    Task<bool> ApplyProviderEventAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        IReadOnlyCollection<string> expectedStatuses,
        string targetCaptureStatus,
        string targetPaymentStatus,
        string providerCaptureReference,
        DateTime eventDateUtc,
        decimal reservedAmountDelta,
        decimal capturedAmountDelta,
        string? failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
}
