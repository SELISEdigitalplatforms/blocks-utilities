using FluentValidation;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentPreflightService
{
    Task<PaymentPreflightResult> ExecuteAsync(
        MakePaymentRequest request,
        string idempotencyKey,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken);
}
