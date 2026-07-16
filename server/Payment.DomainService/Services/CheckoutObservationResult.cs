using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed record CheckoutObservationResult(
    string? RedirectStatus,
    CheckoutCallbackResult? Failure)
{
    public static CheckoutObservationResult Observed(string redirectStatus) =>
        new(redirectStatus, null);

    public static CheckoutObservationResult Failed(CheckoutCallbackResult failure) =>
        new(null, failure);
}
