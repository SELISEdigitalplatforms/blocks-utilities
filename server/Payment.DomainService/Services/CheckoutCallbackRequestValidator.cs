using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class CheckoutCallbackRequestValidator : ICheckoutCallbackRequestValidator
{
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public CheckoutCallbackRequestValidator(IOptionsMonitor<PaymentOptions> options) =>
        _options = options;

    public bool IsValid(CheckoutCallbackRequest request)
    {
        var maximumLength = Math.Clamp(
            _options.CurrentValue.MaximumReturnParameterLength,
            512,
            16_384);

        // Only the signed state and the session id are common to every provider. Adyen also
        // returns an opaque session result; Stripe does not, so requiring it here would
        // reject every Stripe return. Providers that need it enforce it in their own client.
        return !string.IsNullOrWhiteSpace(request.State) &&
               !string.IsNullOrWhiteSpace(request.SessionId) &&
               request.State.Length <= maximumLength &&
               request.SessionId.Length <= 256 &&
               (request.SessionResult == null ||
                request.SessionResult.Length <= maximumLength);
    }
}
