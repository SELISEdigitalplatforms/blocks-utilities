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

        return !string.IsNullOrWhiteSpace(request.State) &&
               !string.IsNullOrWhiteSpace(request.SessionId) &&
               !string.IsNullOrWhiteSpace(request.SessionResult) &&
               request.State.Length <= maximumLength &&
               request.SessionId.Length <= 256 &&
               request.SessionResult.Length <= maximumLength;
    }
}
