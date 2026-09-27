namespace Sms.DomainService.Services;

public interface ISmsRetryPolicy
{
    DateTime GetNextRetryAt(int retryCount, DateTime utcNow);
}
