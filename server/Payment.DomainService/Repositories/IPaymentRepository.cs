using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public interface IPaymentRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);
    Task<PaymentProvider?> GetProviderAsync(string tenantId, string providerName, CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(PaymentDetail payment, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetByIdAsync(string tenantId, string paymentId, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetByPspReferenceAsync(string tenantId, string pspReference, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetRecurringPaymentByOrderIdAsync(
        string tenantId,
        string orderId,
        CancellationToken cancellationToken);
    Task<PaymentDetail?> TryClaimInitiationAsync(string tenantId, string paymentId, string leaseId, DateTime leaseUntilUtc, CancellationToken cancellationToken);
    Task<bool> SaveInitiationRequestAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        Payment.DomainService.Models.HostedCheckout.HostedCheckoutSessionRequest request,
        string frontendResultUrlSnapshot,
        string returnStateNonceHash,
        string shopperReference,
        CancellationToken cancellationToken);
    Task<bool> CompleteInitiationAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string status,
        string? sessionId,
        string? sessionData,
        string? redirectUrl,
        DateTime? expiresAtUtc,
        string? failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
    Task MarkInitiationUnknownAsync(string tenantId, string paymentId, string leaseId, string failureCode, CancellationToken cancellationToken);
    Task<bool> CompleteStoredPaymentChargeInitiationAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string pspReference,
        string? providerResultCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
    Task<bool> SaveProviderRoutingAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string providerReference,
        string merchantAccount,
        CancellationToken cancellationToken);
    Task<bool> SaveCheckoutObservationAsync(
        string tenantId,
        string paymentId,
        string sessionStatus,
        string? resultCode,
        string sessionResultHash,
        string? pspReference,
        PaymentInstrument? instrument,
        CancellationToken cancellationToken);
    Task<bool> ApplyAuthorisationAsync(
        string tenantId,
        string paymentId,
        bool authorized,
        decimal authorizedAmount,
        bool capturedAutomatically,
        string pspReference,
        DateTime eventDateUtc,
        PaymentInstrument? instrument,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
    Task<List<PaymentDetail>> GetPaymentsWithDueOutboxEventsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task<bool> TryClaimOutboxEventAsync(string tenantId, string paymentId, string eventId, string leaseId, DateTime leaseUntilUtc, CancellationToken cancellationToken);
    Task MarkOutboxPublishedAsync(string tenantId, string paymentId, string eventId, string leaseId, DateTime utcNow, CancellationToken cancellationToken);
    Task MarkOutboxFailedAsync(
        string tenantId,
        string paymentId,
        string eventId,
        string leaseId,
        PaymentOutboxStatus status,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string error,
        CancellationToken cancellationToken);
    Task<List<PaymentDetail>> GetStaleInitiationsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task<bool> HasUnresolvedRecurringPaymentAsync(
        string tenantId,
        string storedPaymentMethodId,
        CancellationToken cancellationToken);
}
