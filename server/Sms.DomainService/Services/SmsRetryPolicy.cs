namespace Sms.DomainService.Services;

public class SmsRetryPolicy : ISmsRetryPolicy
{
    public DateTime GetNextRetryAt(int retryCount, DateTime utcNow)
    {
        var safeRetryCount = Math.Clamp(retryCount, 1, 8);
        var delaySeconds = Math.Min(900, Math.Pow(2, safeRetryCount) * 15);
        var jitterSeconds = Random.Shared.Next(0, 20);
        return utcNow.AddSeconds(delaySeconds + jitterSeconds);
    }
}
