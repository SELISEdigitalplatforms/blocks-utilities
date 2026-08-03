using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface ICheckoutCallbackService
{
    Task<CheckoutCallbackResult> ProcessAsync(
        CheckoutCallbackRequest request,
        string clientAddress,
        CancellationToken cancellationToken);
}
