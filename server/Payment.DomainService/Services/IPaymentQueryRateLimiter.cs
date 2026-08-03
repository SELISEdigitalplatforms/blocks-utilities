namespace Payment.DomainService.Services;

public interface IPaymentQueryRateLimiter
{
    Task<PaymentRateLimitResult> CheckAsync(
        string tenantId,
        string actorId,
        CancellationToken cancellationToken);
}
