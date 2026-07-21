namespace Payment.DomainService.Outbox;

public interface IPaymentRefundOutboxProcessor
{
    Task<int> PublishDueAsync(
        string tenantId,
        CancellationToken cancellationToken);
}
