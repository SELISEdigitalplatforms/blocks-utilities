using System.Diagnostics;
using System.Security.Cryptography;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public sealed class PaymentLockRenewalScheduler : IPaymentLockRenewalScheduler
{
    public Task WaitForRenewalAsync(TimeSpan lease, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(250, lease.TotalMilliseconds / 3));
        return Task.Delay(interval, cancellationToken);
    }
}
