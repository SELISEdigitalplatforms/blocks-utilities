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
        return GetCollection<SmsMessage>(message.ProjectKey).ReplaceOneAsync(
            x => x.ItemId == message.ItemId,
            message,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task<SmsMessage?> GetMessageAsync(string projectKey, string messageId, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsMessage>(projectKey).Find(x => x.ItemId == messageId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<SmsMessage?> GetMessageByProviderMessageIdAsync(string projectKey, string providerMessageId, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsMessage>(projectKey).Find(x => x.ProviderMessageId == providerMessageId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task UpdateMessageStatusAsync(string projectKey, string messageId, SmsMessageStatus status, string? providerMessageId = null, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default)
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

        return GetCollection<SmsMessage>(projectKey).UpdateOneAsync(
            x => x.ItemId == messageId,
            Builders<SmsMessage>.Update.Combine(updates),
            cancellationToken: cancellationToken);
    }

    public Task IncrementMessageAttemptAsync(string projectKey, string messageId, CancellationToken cancellationToken = default)
    {
        var update = Builders<SmsMessage>.Update
            .Inc(x => x.AttemptCount, 1)
            .Set(x => x.LastUpdatedDate, DateTime.UtcNow);

        return GetCollection<SmsMessage>(projectKey).UpdateOneAsync(x => x.ItemId == messageId, update, cancellationToken: cancellationToken);
    }

    public Task SaveOutboxAsync(SmsOutboxMessage outbox, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsOutboxMessage>(outbox.ProjectKey).ReplaceOneAsync(
            x => x.ItemId == outbox.ItemId,
            outbox,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task<SmsOutboxMessage?> GetOutboxByMessageIdAsync(string projectKey, string messageId, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsOutboxMessage>(projectKey).Find(x => x.MessageId == messageId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<SmsOutboxMessage>> GetDueOutboxMessagesAsync(DateTime utcNow, int limit, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsOutboxMessage>()
            .Find(x => x.Status == SmsOutboxStatus.RetryScheduled && x.NextVisibleAt <= utcNow)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateOutboxStatusAsync(string projectKey, string outboxId, SmsOutboxStatus status, int? retryCount = null, DateTime? nextVisibleAt = null, string? lastError = null, CancellationToken cancellationToken = default)
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

        return GetCollection<SmsOutboxMessage>(projectKey).UpdateOneAsync(
            x => x.ItemId == outboxId,
            Builders<SmsOutboxMessage>.Update.Combine(updates),
            cancellationToken: cancellationToken);
    }

    public Task SaveAttemptAsync(SmsDeliveryAttempt attempt, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsDeliveryAttempt>(attempt.ProjectKey).InsertOneAsync(attempt, cancellationToken: cancellationToken);
    }

    public Task SaveProviderConfigurationAsync(SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        configuration.LastUpdatedDate = DateTime.UtcNow;
        return GetCollection<SmsProviderConfiguration>(configuration.ProjectKey).ReplaceOneAsync(
            x => x.ItemId == configuration.ItemId,
            configuration,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task<SmsProviderConfiguration?> GetActiveProviderConfigurationAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsProviderConfiguration>(projectKey)
            .Find(x => x.IsEnabled)
            .SortByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.LastUpdatedDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<SmsTemplate?> GetTemplateAsync(string projectKey, string templateName, string language, CancellationToken cancellationToken = default)
    {
        return GetCollection<SmsTemplate>(projectKey)
            .Find(x => x.Name == templateName && x.Language == language)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<long> CountMessagesSinceAsync(string projectKey, string tenantId, DateTime sinceUtc, string? destinationNumber, CancellationToken cancellationToken = default)
    {
        var builder = Builders<SmsMessage>.Filter;
        var filter = builder.Eq(x => x.TenantId, tenantId) & builder.Gte(x => x.CreatedDate, sinceUtc);

        if (!string.IsNullOrWhiteSpace(destinationNumber))
        {
            filter &= builder.AnyEq(x => x.DestinationNumbers, destinationNumber);
        }

        return GetCollection<SmsMessage>(projectKey).CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public Task<List<SmsMessage>> GetSubmittedMessagesOlderThanAsync(DateTime olderThanUtc, int limit, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SmsMessage>.Filter.Eq(x => x.Status, SmsMessageStatus.Submitted) &
                     Builders<SmsMessage>.Filter.Lte(x => x.LastUpdatedDate, olderThanUtc) &
                     Builders<SmsMessage>.Filter.Ne(x => x.ProviderMessageId, null);

        return GetCollection<SmsMessage>()
            .Find(filter)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    private IMongoCollection<T> GetCollection<T>(string? projectKey = null)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            return _dbContextProvider.GetCollection<T>($"{typeof(T).Name}s");
        }

        return _dbContextProvider.GetDatabase(projectKey).GetCollection<T>($"{typeof(T).Name}s");
    }
}
