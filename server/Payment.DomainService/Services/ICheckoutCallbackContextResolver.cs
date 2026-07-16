using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface ICheckoutCallbackContextResolver
{
    Task<CheckoutCallbackContextResolution> ResolveAsync(
        string protectedState,
        string sessionId,
        CancellationToken cancellationToken);
}
