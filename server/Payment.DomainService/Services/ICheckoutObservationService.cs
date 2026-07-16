using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface ICheckoutObservationService
{
    Task<CheckoutObservationResult> ObserveAsync(
        CheckoutCallbackContext context,
        string sessionResult,
        CancellationToken cancellationToken);
}
