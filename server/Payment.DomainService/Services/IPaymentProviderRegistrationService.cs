using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentProviderRegistrationService
{
    /// <summary>
    /// Registers a provider for the calling tenant, once per organization the request names.
    /// The result carries identifiers only; credentials never travel back out.
    /// </summary>
    /// <remarks>
    /// Each organization is attempted independently and reported separately, because each has
    /// its own key ring and its own row: one organization's vault being unreachable is no reason
    /// to discard the configurations that were written for the others.
    /// </remarks>
    Task<PaymentProviderRegistrationResult> RegisterAsync(
        RegisterPaymentProviderRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
