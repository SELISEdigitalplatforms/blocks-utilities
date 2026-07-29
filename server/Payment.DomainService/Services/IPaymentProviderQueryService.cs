using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentProviderQueryService
{
    Task<PaymentProviderListResult> GetProvidersAsync(
        string correlationId,
        CancellationToken cancellationToken);
}
