using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public interface IPaymentWebhookInboxRepository
{
    Task<WebhookStoreResult> StoreAsync(PaymentWebhookInbox webhook, CancellationToken cancellationToken);
    Task<List<PaymentWebhookInbox>> GetDueAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task<PaymentWebhookInbox?> TryClaimAsync(string tenantId, string webhookId, string leaseId, DateTime leaseUntilUtc, CancellationToken cancellationToken);
    Task MarkProcessedAsync(string tenantId, string webhookId, string leaseId, CancellationToken cancellationToken);
    Task MarkFailedAsync(string tenantId, string webhookId, string leaseId, PaymentWebhookStatus status, int attempts, DateTime nextAttemptAtUtc, CancellationToken cancellationToken);
}
