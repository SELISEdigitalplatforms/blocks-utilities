using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class CheckoutUrlPolicy : ICheckoutUrlPolicy
{
    public bool TryResolveHostedUrls(PaymentProvider provider, string signedState, out string returnUrl, out string frontendResultUrl)
    {
        returnUrl = string.Empty;
        frontendResultUrl = string.Empty;
        if (!SafeHttpsUrl.TryParse(provider.ReturnUrl, out var backend) ||
            !SafeHttpsUrl.TryParse(provider.FrontendResultUrl, out var frontend)) return false;

        var builder = new UriBuilder(backend);
        var query = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(query)
            ? $"state={Uri.EscapeDataString(signedState)}"
            : $"{query}&state={Uri.EscapeDataString(signedState)}";
        returnUrl = builder.Uri.AbsoluteUri;
        frontendResultUrl = frontend.AbsoluteUri;
        return true;
    }

    public bool IsAllowedFrontendResultUrl(string value) => SafeHttpsUrl.TryParse(value, out _);
}
