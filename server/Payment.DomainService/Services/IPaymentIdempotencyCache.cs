using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public interface IPaymentIdempotencyCache
{
    Task<string?> GetPaymentIdAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken);
    Task SetPaymentIdAsync(string tenantId, string idempotencyKey, string paymentId, CancellationToken cancellationToken);
}
