using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentProviderCredentialRotationService
{
    Task<PaymentProviderMutationResult> RotateAsync(
        string paymentProviderId,
        RotatePaymentProviderCredentialsRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
