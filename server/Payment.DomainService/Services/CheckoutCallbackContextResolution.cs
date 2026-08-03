using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed record CheckoutCallbackContextResolution(
    CheckoutCallbackContext? Context,
    CheckoutCallbackResult? Failure)
{
    public bool IsSuccess => Context != null;

    public static CheckoutCallbackContextResolution Success(CheckoutCallbackContext context) =>
        new(context, null);

    public static CheckoutCallbackContextResolution Failed(CheckoutCallbackResult failure) =>
        new(null, failure);
}
