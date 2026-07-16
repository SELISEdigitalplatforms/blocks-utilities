using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface ICheckoutUrlPolicy
{
    bool TryResolveHostedUrls(PaymentProvider provider, string signedState, out string returnUrl, out string frontendResultUrl);
    bool IsAllowedFrontendResultUrl(string value);
    bool IsAllowedProviderEndpoint(string value);
}
