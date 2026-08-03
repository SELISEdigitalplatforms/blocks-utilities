namespace Payment.DomainService.Services;

public interface IStoredPaymentMethodRemovalService
{
    Task<StoredPaymentMethodRemovalResult> RemoveStoredPaymentMethodAsync(
        string paymentMethodId,
        string correlationId,
        CancellationToken cancellationToken);
}
