namespace Mail.DomainService.Mails.Services.RateLimiting;

public interface IMailRateLimitCounterStore
{
    Task<MailRateLimitCounterClaimResult> TryClaimAsync(
        MailRateLimitCounterClaim claim,
        CancellationToken cancellationToken = default);
}
