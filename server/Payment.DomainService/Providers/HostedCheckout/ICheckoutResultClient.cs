using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

public interface ICheckoutResultClient
{
    Task<CheckoutResultClientResult> GetAsync(
        PaymentProvider provider,
        string sessionId,
        string sessionResult,
        CancellationToken cancellationToken);
}
