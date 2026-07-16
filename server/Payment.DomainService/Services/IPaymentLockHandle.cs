using System.Diagnostics;
using System.Security.Cryptography;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public interface IPaymentLockHandle : IAsyncDisposable
{
    string Token { get; }
    Task<bool> RenewAsync(CancellationToken cancellationToken);
}
