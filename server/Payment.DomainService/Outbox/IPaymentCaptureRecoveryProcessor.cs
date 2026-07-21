namespace Payment.DomainService.Outbox;

public interface IPaymentCaptureRecoveryProcessor
{
    Task<int> RecoverDueAsync(
        string tenantId,
        CancellationToken cancellationToken);
}
