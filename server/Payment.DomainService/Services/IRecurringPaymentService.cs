using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IRecurringPaymentService
{
    Task<PaymentOperationResult> CreateRecurringPaymentAsync(
        CreateRecurringPaymentRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Charges a card the caller has already resolved from the account that owns it — see
    /// <see cref="PaymentExecutionContext.ChargesOnBehalfOfAccount"/>. In-process only; no
    /// controller may route a request body here.
    /// </summary>
    /// <param name="payerId">Names the paying account, for rate limiting and the audit trail.</param>
    /// <param name="merchantOrganizationId">Where the provider configuration lives.</param>
    Task<PaymentOperationResult> CreateAccountRecurringPaymentAsync(
        CreateRecurringPaymentRequest request,
        string payerId,
        string? merchantOrganizationId,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}
