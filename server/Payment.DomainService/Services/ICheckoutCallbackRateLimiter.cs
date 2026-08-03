using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public interface ICheckoutCallbackRateLimiter
{
    Task<PaymentRateLimitResult> CheckAsync(string clientAddress, string signedState, CancellationToken cancellationToken);
}
