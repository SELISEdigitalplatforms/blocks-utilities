using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IRecurringPaymentPreflightService
{
    Task<RecurringPaymentPreflightResult> ExecuteAsync(
        CreateRecurringPaymentRequest request,
        string idempotencyKey,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken);
}
