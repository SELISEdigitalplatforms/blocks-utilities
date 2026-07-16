using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public interface IStoredPaymentMethodRepository
{
    Task<List<StoredPaymentMethod>> ListActiveAsync(string tenantId, string shopperReference, CancellationToken cancellationToken);
    Task<StoredPaymentMethod?> GetAsync(string tenantId, string itemId, CancellationToken cancellationToken);
    Task UpsertFromProviderAsync(StoredPaymentMethod method, DateTime eventDateUtc, CancellationToken cancellationToken);
    Task MarkDeletionUnknownAsync(string tenantId, string itemId, DateTime nextAttemptAtUtc, CancellationToken cancellationToken);
    Task MarkDisabledAsync(string tenantId, string itemId, DateTime eventDateUtc, CancellationToken cancellationToken);
    Task<List<StoredPaymentMethod>> GetUnknownDeletionsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken);
}
