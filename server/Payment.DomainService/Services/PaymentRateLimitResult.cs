using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using StackExchange.Redis;

namespace Payment.DomainService.Services;

public sealed class PaymentRateLimitResult
{
    public bool IsAllowed { get; init; }
    public bool IsAvailable { get; init; } = true;
    public int Limit { get; init; }
    public int Remaining { get; init; }
    public int RetryAfterSeconds { get; init; }
    public int ResetAfterSeconds { get; init; }

    public bool IsMoreRestrictiveThan(PaymentRateLimitResult other)
    {
        var thisLimit = Math.Max(1, Limit);
        var otherLimit = Math.Max(1, other.Limit);
        var thisWeightedRemaining = (long)Math.Max(0, Remaining) * otherLimit;
        var otherWeightedRemaining = (long)Math.Max(0, other.Remaining) * thisLimit;

        if (thisWeightedRemaining != otherWeightedRemaining)
            return thisWeightedRemaining < otherWeightedRemaining;

        if (Remaining != other.Remaining)
            return Remaining < other.Remaining;

        return ResetAfterSeconds > other.ResetAfterSeconds;
    }
}
