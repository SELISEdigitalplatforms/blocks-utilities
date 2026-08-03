namespace Payment.DomainService.Outbox;

public interface IPaymentRefundRecoveryProcessor
{
    Task<int> RecoverDueAsync(
        string tenantId,
        CancellationToken cancellationToken);
}
