using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public interface IPaymentRateLimiter
{
    Task<PaymentRateLimitResult> CheckAsync(string tenantId, string actor, string orderId, CancellationToken cancellationToken);
}
