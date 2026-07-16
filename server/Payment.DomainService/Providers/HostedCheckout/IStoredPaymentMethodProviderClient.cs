using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.HostedCheckout;

public interface IStoredPaymentMethodProviderClient
{
    Task<ProviderClientOutcome> DeleteAsync(PaymentProvider provider, StoredPaymentMethod method, CancellationToken cancellationToken);
}
