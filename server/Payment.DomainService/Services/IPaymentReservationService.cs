using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface IPaymentReservationService
{
    Task<PaymentReservationResult> ReserveAsync(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}
