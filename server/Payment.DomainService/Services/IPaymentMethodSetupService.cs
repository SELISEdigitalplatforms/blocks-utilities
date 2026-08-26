using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

/// <summary>
/// Collects a card through the provider's hosted page without charging it.
/// </summary>
public interface IPaymentMethodSetupService
{
    /// <summary>
    /// Starts, or resumes, a card-collection session.
    /// </summary>
    /// <remarks>
    /// Idempotent on <paramref name="idempotencyKey"/> the way a charge is: the same key returns
    /// the session already open rather than a second one. A key whose session has failed or
    /// expired reports that failure, so the caller can decide whether to open a fresh attempt
    /// under a new key — this method never silently starts one.
    /// </remarks>
    Task<PaymentOperationResult> CreateSetupAsync(
        CreatePaymentMethodSetupRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}
