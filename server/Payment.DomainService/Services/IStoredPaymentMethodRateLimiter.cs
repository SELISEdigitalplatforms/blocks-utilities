namespace Payment.DomainService.Services;

public interface IStoredPaymentMethodRateLimiter
{
    Task<PaymentRateLimitResult> CheckListAsync(
        string tenantId,
        string actorId,
        CancellationToken cancellationToken);

    Task<PaymentRateLimitResult> CheckRemovalAsync(
        string tenantId,
        string actorId,
        CancellationToken cancellationToken);
}
