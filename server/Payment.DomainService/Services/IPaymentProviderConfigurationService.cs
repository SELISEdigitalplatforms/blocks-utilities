using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentProviderConfigurationService
{
    Task<PaymentProviderMutationResult> UpdateAsync(
        string paymentProviderId,
        UpdatePaymentProviderRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
