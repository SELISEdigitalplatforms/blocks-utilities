using Blocks.Genesis;
using MongoDB.Driver;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Repositories;

public class SmsRepository : ISmsRepository
{
    private readonly IDbContextProvider _dbContextProvider;

    public SmsRepository(IDbContextProvider dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public Task SaveMessageAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsMessage>(message.TenantId).ReplaceOneAsync(
            x => x.ItemId == message.ItemId,
            message,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task<SmsMessage?> GetMessageAsync(string messageId, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        return GetCollection<SmsMessage>(tenantId).Find(x => x.ItemId == messageId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<SmsMessage?> GetMessageByProviderMessageIdAsync(string providerMessageId, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        return GetCollection<SmsMessage>(tenantId).Find(x => x.ProviderMessageId == providerMessageId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task UpdateMessageStatusAsync(string messageId, SmsMessageStatus status, string? providerMessageId = null, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        var updates = new List<UpdateDefinition<SmsMessage>>
        {
            Builders<SmsMessage>.Update.Set(x => x.Status, status),
            Builders<SmsMessage>.Update.Set(x => x.LastUpdatedDate, DateTime.UtcNow)
        };

        if (!string.IsNullOrWhiteSpace(providerMessageId))
        {
            updates.Add(Builders<SmsMessage>.Update.Set(x => x.ProviderMessageId, providerMessageId));
        }

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            updates.Add(Builders<SmsMessage>.Update.Set(x => x.LastErrorCode, errorCode));
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            updates.Add(Builders<SmsMessage>.Update.Set(x => x.LastErrorMessage, SmsLogSanitizer.SanitizeError(errorMessage)));
        }

        return GetCollection<SmsMessage>(tenantId).UpdateOneAsync(
            x => x.ItemId == messageId,
            Builders<SmsMessage>.Update.Combine(updates),
            cancellationToken: cancellationToken);
    }

    public Task IncrementMessageAttemptAsync(string messageId, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        var update = Builders<SmsMessage>.Update
            .Inc(x => x.AttemptCount, 1)
            .Set(x => x.LastUpdatedDate, DateTime.UtcNow);

        return GetCollection<SmsMessage>(tenantId).UpdateOneAsync(x => x.ItemId == messageId, update, cancellationToken: cancellationToken);
    }

    public Task SaveOutboxAsync(SmsOutboxMessage outbox, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsOutboxMessage>(outbox.TenantId).ReplaceOneAsync(
            x => x.ItemId == outbox.ItemId,
            outbox,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task<SmsOutboxMessage?> GetOutboxByMessageIdAsync(string messageId, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        return GetCollection<SmsOutboxMessage>(tenantId).Find(x => x.MessageId == messageId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<SmsOutboxMessage>> GetDueOutboxMessagesAsync(DateTime utcNow, int limit, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        return GetCollection<SmsOutboxMessage>(tenantId)
            .Find(x => x.Status == SmsOutboxStatus.RetryScheduled && x.NextVisibleAt <= utcNow)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateOutboxStatusAsync(string outboxId, SmsOutboxStatus status, int? retryCount = null, DateTime? nextVisibleAt = null, string? lastError = null, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        var updates = new List<UpdateDefinition<SmsOutboxMessage>>
        {
            Builders<SmsOutboxMessage>.Update.Set(x => x.Status, status),
            Builders<SmsOutboxMessage>.Update.Set(x => x.LastUpdatedDate, DateTime.UtcNow)
        };

        if (retryCount.HasValue)
        {
            updates.Add(Builders<SmsOutboxMessage>.Update.Set(x => x.RetryCount, retryCount.Value));
        }

        if (nextVisibleAt.HasValue)
        {
            updates.Add(Builders<SmsOutboxMessage>.Update.Set(x => x.NextVisibleAt, nextVisibleAt.Value));
        }

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            updates.Add(Builders<SmsOutboxMessage>.Update.Set(x => x.LastError, SmsLogSanitizer.SanitizeError(lastError)));
        }

        return GetCollection<SmsOutboxMessage>(tenantId).UpdateOneAsync(
            x => x.ItemId == outboxId,
            Builders<SmsOutboxMessage>.Update.Combine(updates),
            cancellationToken: cancellationToken);
    }

    public Task SaveAttemptAsync(SmsDeliveryAttempt attempt, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsDeliveryAttempt>(attempt.TenantId).InsertOneAsync(attempt, cancellationToken: cancellationToken);
    }

    public Task SaveProviderConfigurationAsync(SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        configuration.LastUpdatedDate = DateTime.UtcNow;
        return GetCollection<SmsProviderConfiguration>(configuration.TenantId).ReplaceOneAsync(
            x => x.ItemId == configuration.ItemId,
            configuration,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task<SmsProviderConfiguration?> GetActiveProviderConfigurationAsync(CancellationToken cancellationToken = default, string? tenantId = null)
    {
        return GetCollection<SmsProviderConfiguration>(tenantId)
            .Find(x => x.IsEnabled)
            .SortByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.LastUpdatedDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<SmsTemplate?> GetTemplateAsync(string templateName, string language, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        return GetCollection<SmsTemplate>(tenantId)
            .Find(x => x.Name == templateName && x.Language == language)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<long> CountMessagesSinceAsync(DateTime sinceUtc, string? destinationNumber, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        var resolvedTenantId = ResolveTenantId(tenantId);
        var builder = Builders<SmsMessage>.Filter;
        var filter = builder.Eq(x => x.TenantId, resolvedTenantId) & builder.Gte(x => x.CreatedDate, sinceUtc);

        if (!string.IsNullOrWhiteSpace(destinationNumber))
        {
            filter &= builder.AnyEq(x => x.DestinationNumbers, destinationNumber);
        }

        return GetCollection<SmsMessage>(resolvedTenantId).CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public Task<List<SmsMessage>> GetSubmittedMessagesOlderThanAsync(DateTime olderThanUtc, int limit, CancellationToken cancellationToken = default, string? tenantId = null)
    {
        var filter = Builders<SmsMessage>.Filter.Eq(x => x.Status, SmsMessageStatus.Submitted) &
                     Builders<SmsMessage>.Filter.Lte(x => x.LastUpdatedDate, olderThanUtc) &
                     Builders<SmsMessage>.Filter.Ne(x => x.ProviderMessageId, null);

        return GetCollection<SmsMessage>(tenantId)
            .Find(filter)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    private IMongoCollection<T> GetCollection<T>(string? tenantId = null)
    {
        return _dbContextProvider.GetDatabase(ResolveTenantId(tenantId)).GetCollection<T>($"{typeof(T).Name}s");
    }

    private static string ResolveTenantId(string? tenantId)
    {
        var resolvedTenantId = !string.IsNullOrWhiteSpace(tenantId)
            ? tenantId
            : BlocksContext.GetContext()?.TenantId;

        if (string.IsNullOrWhiteSpace(resolvedTenantId))
        {
            throw new InvalidOperationException("SMS repository requires a tenant id or an active BlocksContext tenant.");
        }

        return resolvedTenantId;
    }
}
