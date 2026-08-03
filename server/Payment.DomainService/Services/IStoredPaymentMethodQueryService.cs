namespace Payment.DomainService.Services;

public interface IStoredPaymentMethodQueryService
{
    Task<StoredPaymentMethodQueryResult> GetStoredPaymentMethodsAsync(
        string correlationId,
        CancellationToken cancellationToken);
}
