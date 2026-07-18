namespace Payment.DomainService.Services;

public interface IStoredPaymentMethodRemovalRecoveryProcessor
{
    Task<int> RecoverDueRemovalsAsync(
        string tenantId,
        CancellationToken cancellationToken);
}
