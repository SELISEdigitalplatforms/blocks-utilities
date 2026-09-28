using Sms.DomainService.Entities;

namespace Sms.DomainService.Services;

public interface ISmsRateLimiter
{
    Task<SmsRateLimitResult> CheckAsync(string tenantId, IReadOnlyCollection<string> destinationNumbers, SmsRateLimitSettings settings, CancellationToken cancellationToken = default);
}

public class SmsRateLimitResult
{
    public bool IsAllowed { get; set; }
    public string? Reason { get; set; }

    public static SmsRateLimitResult Allowed() => new() { IsAllowed = true };

    public static SmsRateLimitResult Blocked(string reason) => new() { IsAllowed = false, Reason = reason };
}
