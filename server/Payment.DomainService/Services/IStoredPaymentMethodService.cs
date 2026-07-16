using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface IStoredPaymentMethodService
{
    Task<StoredPaymentMethodOperationResult> ListAsync(string correlationId, CancellationToken cancellationToken);
    Task<StoredPaymentMethodOperationResult> DeleteAsync(string paymentMethodId, string correlationId, CancellationToken cancellationToken);
}
