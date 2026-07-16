using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface IPaymentWebhookProcessor
{
    Task<int> ProcessDueAsync(string tenantId, CancellationToken cancellationToken);
}
