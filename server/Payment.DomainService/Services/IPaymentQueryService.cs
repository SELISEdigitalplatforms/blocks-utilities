using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IPaymentQueryService
{
    Task<PaymentQueryOperationResult> GetPaymentsAsync(
        GetPaymentsRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
