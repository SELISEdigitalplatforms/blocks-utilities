using Blocks.Genesis;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Repositories;

/// <summary>Everything here lives in the tenant's own database, named by tenant id.</summary>
public class SmsRepository : ISmsRepository
{
    private static readonly SmsMessageStatus[] SendableStatuses =
        [SmsMessageStatus.Accepted, SmsMessageStatus.Queued, SmsMessageStatus.RetryScheduled];

    private readonly IDbContextProvider _dbContextProvider;

    public SmsRepository(IDbContextProvider dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public Task SaveMessageAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        return Messages(message.TenantId).ReplaceOneAsync(
            x => x.ItemId == message.ItemId,
            message,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task<SmsMessage?> GetMessageAsync(string tenantId, string messageId, CancellationToken cancellationToken = default)
    {
        return Messages(tenantId).Find(x => x.ItemId == messageId).FirstOrDefaultAsync(cancellationToken)!;
    }

    public Task MarkQueuedAsync(string tenantId, string messageId, CancellationToken cancellationToken = default)
    {
        return Messages(tenantId).UpdateOneAsync(
            x => x.ItemId == messageId && x.Status == SmsMessageStatus.Accepted,
            Builders<SmsMessage>.Update
                .Set(x => x.Status, SmsMessageStatus.Queued)
                .Set(x => x.LastUpdatedDate, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    public Task<SmsMessage?> TryClaimForSendAsync(string tenantId, string messageId, string leaseId, DateTime utcNow, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SmsMessage>.Filter;
        var claimable = filter.Eq(x => x.ItemId, messageId) & (
            filter.In(x => x.Status, SendableStatuses) |
            // A worker that died mid-send: its lease lapses and the message becomes claimable again.
            // Recipients it already submitted are persisted, so the reclaim does not resend them.
            (filter.Eq(x => x.Status, SmsMessageStatus.Processing) & filter.Lt(x => x.LeaseExpiresAt, utcNow)));

        return Messages(tenantId).FindOneAndUpdateAsync(
            claimable,
            Builders<SmsMessage>.Update
                .Set(x => x.Status, SmsMessageStatus.Processing)
                .Set(x => x.LeaseId, leaseId)
                .Set(x => x.LeaseExpiresAt, utcNow.Add(leaseDuration))
                .Inc(x => x.AttemptCount, 1)
                .Set(x => x.LastUpdatedDate, utcNow),
            new FindOneAndUpdateOptions<SmsMessage> { ReturnDocument = ReturnDocument.After },
            cancellationToken)!;
    }

    public Task UpdateRecipientAsync(string tenantId, string messageId, string leaseId, SmsRecipient recipient, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SmsMessage>.Filter;
        return Messages(tenantId).UpdateOneAsync(
            filter.Eq(x => x.ItemId, messageId) &
            filter.Eq(x => x.LeaseId, leaseId) &
            filter.ElemMatch(x => x.Recipients, r => r.Number == recipient.Number),
            Builders<SmsMessage>.Update
                .Set(x => x.Recipients.FirstMatchingElement(), recipient)
                .Set(x => x.LastUpdatedDate, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    public Task CompleteSendRoundAsync(string tenantId, string messageId, string leaseId, SmsMessageStatus status, string? errorCode, string? errorMessage, CancellationToken cancellationToken = default)
    {
        var update = Builders<SmsMessage>.Update
            .Set(x => x.Status, status)
            .Set(x => x.LeaseId, null)
            .Set(x => x.LeaseExpiresAt, null)
            .Set(x => x.LastUpdatedDate, DateTime.UtcNow)
            .Set(x => x.LastErrorCode, errorCode)
            .Set(x => x.LastErrorMessage, SmsLogSanitizer.SanitizeError(errorMessage));

        return Messages(tenantId).UpdateOneAsync(
            x => x.ItemId == messageId && x.LeaseId == leaseId,
            update,
            cancellationToken: cancellationToken);
    }

    public Task SetStatusAsync(string tenantId, string messageId, SmsMessageStatus status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        var update = Builders<SmsMessage>.Update
            .Set(x => x.Status, status)
            .Set(x => x.LastUpdatedDate, DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            update = update
                .Set(x => x.LastErrorCode, errorCode)
                .Set(x => x.LastErrorMessage, SmsLogSanitizer.SanitizeError(errorMessage));
        }

        return Messages(tenantId).UpdateOneAsync(x => x.ItemId == messageId, update, cancellationToken: cancellationToken);
    }

    public Task IncrementDeliveryCheckAsync(string tenantId, string messageId, CancellationToken cancellationToken = default)
    {
        return Messages(tenantId).UpdateOneAsync(
            x => x.ItemId == messageId,
            Builders<SmsMessage>.Update.Inc(x => x.DeliveryCheckCount, 1),
            cancellationToken: cancellationToken);
    }

    public Task<SmsMessage?> GetMessageByProviderMessageIdAsync(string tenantId, string providerMessageId, CancellationToken cancellationToken = default)
    {
        return Messages(tenantId)
            .Find(Builders<SmsMessage>.Filter.ElemMatch(x => x.Recipients, r => r.ProviderMessageId == providerMessageId))
            .FirstOrDefaultAsync(cancellationToken)!;
    }

    public async Task<bool> ApplyRecipientDeliveryAsync(string tenantId, string messageId, string providerMessageId, SmsRecipientStatus status, string? errorCode, string? errorMessage, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SmsMessage>.Filter;
        var now = DateTime.UtcNow;
        var result = await Messages(tenantId).UpdateOneAsync(
            filter.Eq(x => x.ItemId, messageId) &
            filter.ElemMatch(x => x.Recipients, r => r.ProviderMessageId == providerMessageId && r.Status == SmsRecipientStatus.Submitted),
            Builders<SmsMessage>.Update
                .Set(x => x.Recipients.FirstMatchingElement().Status, status)
                .Set(x => x.Recipients.FirstMatchingElement().ErrorCode, errorCode)
                .Set(x => x.Recipients.FirstMatchingElement().ErrorMessage, SmsLogSanitizer.SanitizeError(errorMessage))
                .Set(x => x.Recipients.FirstMatchingElement().LastUpdatedDate, now)
                .Set(x => x.LastUpdatedDate, now),
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    public Task SaveAttemptAsync(SmsDeliveryAttempt attempt, CancellationToken cancellationToken = default)
    {
        return Collection<SmsDeliveryAttempt>(attempt.TenantId).InsertOneAsync(attempt, cancellationToken: cancellationToken);
    }

    public Task SaveProviderConfigurationAsync(SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        configuration.LastUpdatedDate = DateTime.UtcNow;
        return Configurations(configuration.TenantId).ReplaceOneAsync(
            x => x.ItemId == configuration.ItemId,
            configuration,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public Task<SmsProviderConfiguration?> GetProviderConfigurationAsync(string tenantId, string configurationId, CancellationToken cancellationToken = default)
    {
        return Configurations(tenantId).Find(x => x.ItemId == configurationId).FirstOrDefaultAsync(cancellationToken)!;
    }

    public Task<SmsProviderConfiguration?> GetActiveProviderConfigurationAsync(string tenantId, SmsProviderType? providerType = null, CancellationToken cancellationToken = default)
    {
        return Configurations(tenantId)
            .Find(x => x.IsEnabled && (providerType == null || x.ProviderType == providerType))
            .SortByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.LastUpdatedDate)
            .FirstOrDefaultAsync(cancellationToken)!;
    }

    public Task ClearOtherDefaultsAsync(string tenantId, string keepConfigurationId, CancellationToken cancellationToken = default)
    {
        return Configurations(tenantId).UpdateManyAsync(
            x => x.ItemId != keepConfigurationId && x.IsDefault,
            Builders<SmsProviderConfiguration>.Update.Set(x => x.IsDefault, false),
            cancellationToken: cancellationToken);
    }

    public Task<SmsTemplate?> GetTemplateAsync(string tenantId, string templateName, string language, CancellationToken cancellationToken = default)
    {
        return Collection<SmsTemplate>(tenantId)
            .Find(x => x.Name == templateName && x.Language == language)
            .FirstOrDefaultAsync(cancellationToken)!;
    }

    private IMongoCollection<SmsMessage> Messages(string tenantId) => Collection<SmsMessage>(tenantId);

    private IMongoCollection<SmsProviderConfiguration> Configurations(string tenantId) => Collection<SmsProviderConfiguration>(tenantId);

    private IMongoCollection<T> Collection<T>(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // Never fall through to a default database: that is how a tenant's SMS became invisible
            // to the worker before.
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        }

        return _dbContextProvider.GetDatabase(tenantId).GetCollection<T>($"{typeof(T).Name}s");
    }
}
