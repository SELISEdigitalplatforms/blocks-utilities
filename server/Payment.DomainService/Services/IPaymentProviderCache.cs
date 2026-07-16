using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface IPaymentProviderCache
{
    Task<PaymentProvider?> GetAsync(string tenantId, string providerName, Func<Task<PaymentProvider?>> loader);
    void Remove(string tenantId, string providerName);
}
