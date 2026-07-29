using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentProviderRegistrationService
{
    /// <summary>
    /// Registers a provider for the calling tenant. The result carries identifiers only;
    /// credentials never travel back out.
    /// </summary>
    Task<PaymentOperationResult> RegisterAsync(
        RegisterPaymentProviderRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
