using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface IPaymentStateTransitionService
{
    Task<PaymentOperationResult> ApplyProviderResultAsync(
        PaymentDetail payment,
        ProviderSessionCreationResult providerResult,
        string leaseId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<PaymentOperationResult> CompleteFailureAsync(
        PaymentDetail payment,
        string leaseId,
        PaymentFailureKind failureKind,
        string failureCode,
        string safeMessage,
        string correlationId,
        CancellationToken cancellationToken);
}
