namespace Payment.DomainService.Services;

public interface IStoredPaymentMethodQueryService
{
    /// <param name="requestedOrganizationId">
    /// An organization named by the caller, honoured only for the console. See
    /// <c>PaymentOrganizationScope</c>.
    /// </param>
    Task<StoredPaymentMethodQueryResult> GetStoredPaymentMethodsAsync(
        string? requestedOrganizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
