using System.Text;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface IPaymentInitiationService
{
    Task<PaymentOperationResult> InitiateAsync(
        MakePaymentRequest request,
        PaymentExecutionContext context,
        PaymentDetail payment,
        string leaseId,
        long minorUnits,
        string correlationId,
        CancellationToken cancellationToken);

    Task RecoverAsync(PaymentDetail payment, CancellationToken cancellationToken);
}
