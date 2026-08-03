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

    /// <summary>
    /// Completes a capture that the provider settled during the call itself, in one write:
    /// the terminal status, the captured amounts, and the released lease.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CompleteSubmissionAsync"/> because a provider with no capture
    /// object never sends an event naming the capture, so there is nothing to await. Holds the
    /// same lease filter, so a concurrent worker cannot apply it twice.
    /// </remarks>
    Task<bool> CompleteSettlementAsync(
        string tenantId,
        string paymentDetailId,
        string captureId,
        string leaseId,
        string providerCaptureReference,
        string? providerStatus,
        string targetPaymentStatus,
        decimal amount,
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

    /// <summary>
    /// Records a capture this service never requested — one made in the provider's own
    /// dashboard — against the payment alone, since there is no capture record to settle.
    /// </summary>
    /// <remarks>
    /// Adds to the captured amount rather than replacing it, so several partial captures made
    /// outside this service accumulate. Replays are excluded by the outbox deduplication key,
    /// which is what stops the addition being applied twice.
    /// </remarks>
    Task<bool> ApplyExternalCaptureAsync(
        string tenantId,
        string paymentDetailId,
        string targetPaymentStatus,
        decimal capturedAmount,
        string providerCaptureReference,
        DateTime eventDateUtc,
        PaymentOutboxEvent outboxEvent,
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
