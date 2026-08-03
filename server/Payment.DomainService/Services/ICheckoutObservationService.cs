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
    /// <param name="sessionResult">
    /// The opaque result token the provider returned on the redirect, where it issues one.
    /// Null for providers that identify the session by id alone, such as Stripe.
    /// </param>
    Task<CheckoutObservationResult> ObserveAsync(
        CheckoutCallbackContext context,
        string? sessionResult,
        CancellationToken cancellationToken);
}
