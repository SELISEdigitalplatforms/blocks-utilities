using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Mail.DomainService.Services
{
    public class MailRepository : IMailRepository
    {
        private const string LastAccumulator = "$last";
        private static readonly SemaphoreSlim RateLimitIndexLock = new(1, 1);
        private static bool _rateLimitIndexesEnsured;
        private readonly IDbContextProvider _dbContextProvider;

        public MailRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public IMongoCollection<T> GetCollection<T>()
        {
            return _dbContextProvider.GetCollection<T>($"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollection<T>(string tenantId)
        {
            return _dbContextProvider.GetCollection<T>(tenantId, $"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollectionByName<T>(string collectionName)
        {
            return _dbContextProvider.GetCollection<T>(collectionName);
        }

        public Task<bool> MailTemplateForPurposeExists(string purpose, string language)
        {
            var collection = GetCollection<EmailTemplate>();

            return collection.Find(t => t.Name == purpose && t.Language == language).AnyAsync();
        }

        public async Task<bool> MailServerConfigurationExists(string purpose, string language)
        {
            var emailTemplateCollection = GetCollection<EmailTemplate>();

            var template = await emailTemplateCollection.Find(t => t.Name == purpose && t.Language == language).FirstOrDefaultAsync();

            if (template == null)
            {
                return false;
            }

            var mailConfigurationId = template.MailConfigurationId;

            var mailServerConfigurationCollection = GetCollection<MailServerConfiguration>();

            var mailConfigurationExists = await mailServerConfigurationCollection.Find(sc => sc.ItemId == mailConfigurationId).AnyAsync();

            return mailConfigurationExists;
        }

        public Task<bool> FileExists(string fileId)
        {
            var filesCollection = GetCollectionByName<BsonDocument>("Files");

            var filter = Builders<BsonDocument>.Filter.Eq("_id", fileId);

            return filesCollection.Find(filter).AnyAsync();
        }
        public async Task<List<string>> GetEmailAdressOfUsers(IEnumerable<string> emails)
        {
            if (emails == null || !emails.Any())
            {
                return new List<string>();
            }

            var collection = GetCollectionByName<BsonDocument>("Users");

            var filter = Builders<BsonDocument>.Filter.In("Email", emails);

            var projection = Builders<BsonDocument>.Projection.Include("Email");

            var results = await collection.Find(filter).Project(projection).ToListAsync();

            return results.Select(doc => doc["Email"].AsString).Distinct().ToList();
        }
        public Task<MailServerConfiguration> GetMailServerConfigurationByTenantId(string tenantId)
        {
            var mailServerConfigurationCollection = GetCollection<MailServerConfiguration>();

            return mailServerConfigurationCollection.Find(_ => true).FirstOrDefaultAsync();
        }
        public Task<EmailTemplate> GetEmailTemplateByPurpose(string purpose, string language, string organizationId)
        {
            return GetEmailTemplate(purpose, language, organizationId);
        }

        private async Task<EmailTemplate> GetEmailTemplate(string purpose, string language, string organizationId)
        {
            var emailTemplateCollection = GetCollection<EmailTemplate>();
            EmailTemplate emailTemplate = null;

            if (!string.IsNullOrWhiteSpace(organizationId))
            {
                var nameLanguageOrganizationFilter =
                    Builders<EmailTemplate>.Filter.Eq(t => t.Name, purpose) &
                    Builders<EmailTemplate>.Filter.Eq(t => t.Language, language) &
                    Builders<EmailTemplate>.Filter.In("OrganizationIds", new[] { organizationId });

                emailTemplate = await emailTemplateCollection.Find(nameLanguageOrganizationFilter).FirstOrDefaultAsync();

                if (emailTemplate != null)
                {
                    return emailTemplate;
                }
            }

            var nameLanguageFilter =
               Builders<EmailTemplate>.Filter.Eq(t => t.Name, purpose) &
               Builders<EmailTemplate>.Filter.Eq(t => t.Language, language);

            emailTemplate = await emailTemplateCollection.Find(nameLanguageFilter).FirstOrDefaultAsync();

            return emailTemplate;
        }

        public async Task<MailServerConfiguration> GetMailServerConfigurationByPurpose(string purpose, string language, string organizationId)
        {
            var template = await GetEmailTemplate(purpose, language, organizationId);

            if (template == null)
            {
                return null;
            }

            var mailConfigurationId = template.MailConfigurationId;

            var mailServerConfigurationCollection = GetCollection<MailServerConfiguration>();

            var mailServerConfiguration = await mailServerConfigurationCollection.Find(sc => sc.ItemId == mailConfigurationId).FirstOrDefaultAsync();

            return mailServerConfiguration;
        }

        public async Task<bool> SaveMailToBeSent(MailToBeSent mailToBeSent)
        {
            var collection = GetCollection<MailToBeSent>();

            await collection.InsertOneAsync(mailToBeSent);

            return true;
        }

        public async Task<bool> SaveMailToBeSentWithOutboxAsync(MailToBeSent mailToBeSent, MailOutboxMessage outboxMessage)
        {
            var mailCollection = GetCollection<MailToBeSent>();
            var outboxCollection = GetCollection<MailOutboxMessage>();

            using var session = await mailCollection.Database.Client.StartSessionAsync();
            session.StartTransaction();

            try
            {
                await mailCollection.InsertOneAsync(session, mailToBeSent);
                await outboxCollection.InsertOneAsync(session, outboxMessage);
                await session.CommitTransactionAsync();
                return true;
            }
            catch
            {
                await session.AbortTransactionAsync();
                throw;
            }
        }

        public async Task<MailToBeSent> GetMailToBeSent(string itemId)
        {
            var collection = GetCollection<MailToBeSent>();

            var result = await collection.Find(x => x.ItemId == itemId).FirstOrDefaultAsync();
            return result;
        }

        public async Task<bool> TryStartMailSubmissionAsync(string itemId, DateTime startedAtUtc, int processingLockTimeoutMinutes)
        {
            var collection = GetCollection<MailToBeSent>();
            var expiredProcessingThreshold = startedAtUtc.AddMinutes(-Math.Max(1, processingLockTimeoutMinutes));

            var filter = Builders<MailToBeSent>.Filter.And(
                Builders<MailToBeSent>.Filter.Eq(x => x.ItemId, itemId),
                Builders<MailToBeSent>.Filter.Or(
                    Builders<MailToBeSent>.Filter.Eq(x => x.SubmissionStatus, MailSubmissionStatus.Queued),
                    Builders<MailToBeSent>.Filter.Eq(x => x.SubmissionStatus, MailSubmissionStatus.FailedRetryable),
                    Builders<MailToBeSent>.Filter.And(
                        Builders<MailToBeSent>.Filter.Eq(x => x.SubmissionStatus, MailSubmissionStatus.Processing),
                        Builders<MailToBeSent>.Filter.Lt(x => x.LastSubmissionAttemptAtUtc, expiredProcessingThreshold))));

            var update = Builders<MailToBeSent>.Update
                .Set(x => x.SubmissionStatus, MailSubmissionStatus.Processing)
                .Set(x => x.LastSubmissionAttemptAtUtc, startedAtUtc)
                .Inc(x => x.SubmissionAttemptCount, 1);

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount == 1;
        }

        public async Task UpdateMailSubmissionAcceptedAsync(
            string itemId,
            string internetMessageId,
            DateTime submittedAtUtc,
            string senderAddress,
            IEnumerable<MailRecipientDeliveryStatus> recipientStatuses,
            MailSubmissionResult submissionResult)
        {
            var collection = GetCollection<MailToBeSent>();
            var update = Builders<MailToBeSent>.Update
                .Set(x => x.SubmissionStatus, MailSubmissionStatus.Accepted)
                .Set(x => x.InternetMessageId, internetMessageId)
                .Set(x => x.SubmittedAtUtc, submittedAtUtc)
                .Set(x => x.SenderAddress, senderAddress)
                .Set(x => x.RecipientDeliveryStatuses, recipientStatuses.ToList())
                .Set(x => x.LastProviderStatusCode, submissionResult.ProviderStatusCode)
                .Set(x => x.LastProviderRequestId, submissionResult.ProviderRequestId)
                .Set(x => x.LastSubmissionFailureReason, null);

            await collection.UpdateOneAsync(x => x.ItemId == itemId, update);
        }

        public async Task UpdateMailSubmissionFailedAsync(string itemId, MailSubmissionStatus status, MailSubmissionResult submissionResult)
        {
            var collection = GetCollection<MailToBeSent>();
            var update = Builders<MailToBeSent>.Update
                .Set(x => x.SubmissionStatus, status)
                .Set(x => x.LastProviderStatusCode, submissionResult.ProviderStatusCode)
                .Set(x => x.LastProviderRequestId, submissionResult.ProviderRequestId)
                .Set(x => x.LastSubmissionFailureReason, submissionResult.FailureReason);

            await collection.UpdateOneAsync(x => x.ItemId == itemId, update);
        }

        public async Task UpdateMailSubmissionTrackingAsync(string itemId, string internetMessageId, DateTime submittedAtUtc, string senderAddress, IEnumerable<MailRecipientDeliveryStatus> recipientStatuses)
        {
            var collection = GetCollection<MailToBeSent>();
            var update = Builders<MailToBeSent>.Update
                .Set(x => x.SubmissionStatus, MailSubmissionStatus.Accepted)
                .Set(x => x.InternetMessageId, internetMessageId)
                .Set(x => x.SubmittedAtUtc, submittedAtUtc)
                .Set(x => x.SenderAddress, senderAddress)
                .Set(x => x.RecipientDeliveryStatuses, recipientStatuses.ToList());

            await collection.UpdateOneAsync(x => x.ItemId == itemId, update);
        }

        public async Task UpdateMailRecipientDeliveryStatusAsync(string itemId, string recipient, MailStatus status, string? statusReason, DateTime checkedAtUtc)
        {
            var collection = GetCollection<MailToBeSent>();
            var filter = Builders<MailToBeSent>.Filter.And(
                Builders<MailToBeSent>.Filter.Eq(x => x.ItemId, itemId),
                Builders<MailToBeSent>.Filter.ElemMatch(x => x.RecipientDeliveryStatuses, x => x.Recipient == recipient));

            var update = Builders<MailToBeSent>.Update
                .Set("RecipientDeliveryStatuses.$.Status", status)
                .Set("RecipientDeliveryStatuses.$.StatusReason", statusReason)
                .Set("RecipientDeliveryStatuses.$.CheckedAtUtc", checkedAtUtc);

            await collection.UpdateOneAsync(filter, update);
        }

        public async Task InsertOutboxMessageAsync(MailOutboxMessage outboxMessage)
        {
            var collection = GetCollection<MailOutboxMessage>();
            var existingMessage = await collection
                .Find(x => x.DeduplicationKey == outboxMessage.DeduplicationKey)
                .FirstOrDefaultAsync();

            if (existingMessage != null)
            {
                return;
            }

            await collection.InsertOneAsync(outboxMessage);
        }

        public async Task<IReadOnlyList<MailOutboxMessage>> GetPendingOutboxMessagesAsync(DateTime utcNow, int batchSize)
        {
            var collection = GetCollection<MailOutboxMessage>();
            var filter = Builders<MailOutboxMessage>.Filter.And(
                Builders<MailOutboxMessage>.Filter.In(x => x.Status, [OutboxMessageStatus.Pending, OutboxMessageStatus.FailedRetryable]),
                Builders<MailOutboxMessage>.Filter.Lte(x => x.NextAttemptUtc, utcNow));

            return await collection.Find(filter)
                .SortBy(x => x.CreatedAtUtc)
                .Limit(batchSize)
                .ToListAsync();
        }

        public async Task<bool> TryClaimOutboxMessageAsync(string itemId, DateTime claimedAtUtc)
        {
            var collection = GetCollection<MailOutboxMessage>();
            var filter = Builders<MailOutboxMessage>.Filter.And(
                Builders<MailOutboxMessage>.Filter.Eq(x => x.ItemId, itemId),
                Builders<MailOutboxMessage>.Filter.In(x => x.Status, [OutboxMessageStatus.Pending, OutboxMessageStatus.FailedRetryable]),
                Builders<MailOutboxMessage>.Filter.Lte(x => x.NextAttemptUtc, claimedAtUtc));

            var update = Builders<MailOutboxMessage>.Update.Set(x => x.Status, OutboxMessageStatus.Publishing);
            var result = await collection.UpdateOneAsync(filter, update);

            return result.ModifiedCount == 1;
        }

        public async Task MarkOutboxMessagePublishedAsync(string itemId, DateTime publishedAtUtc)
        {
            var collection = GetCollection<MailOutboxMessage>();
            var update = Builders<MailOutboxMessage>.Update
                .Set(x => x.Status, OutboxMessageStatus.Published)
                .Set(x => x.PublishedAtUtc, publishedAtUtc)
                .Set(x => x.LastError, null);

            await collection.UpdateOneAsync(x => x.ItemId == itemId, update);
        }

        public async Task MarkOutboxMessageFailedAsync(string itemId, int attemptCount, DateTime nextAttemptUtc, OutboxMessageStatus status, string lastError)
        {
            var collection = GetCollection<MailOutboxMessage>();
            var update = Builders<MailOutboxMessage>.Update
                .Set(x => x.Status, status)
                .Set(x => x.AttemptCount, attemptCount)
                .Set(x => x.NextAttemptUtc, nextAttemptUtc)
                .Set(x => x.LastError, lastError);

            await collection.UpdateOneAsync(x => x.ItemId == itemId, update);
        }

        public async Task<MailRateLimitCounterClaimResult> TryIncrementRateLimitCounterAsync(MailRateLimitCounterClaim claim)
        {
            await EnsureRateLimitCounterIndexesAsync();

            var collection = GetCollection<MailRateLimitCounter>();
            var cost = Math.Max(1, claim.Cost);
            var limit = Math.Max(1, claim.Limit);

            if (cost > limit)
            {
                return new MailRateLimitCounterClaimResult
                {
                    IsAllowed = false,
                    Used = limit,
                    Limit = limit,
                    WindowEndUtc = claim.WindowEndUtc
                };
            }

            var filter = Builders<MailRateLimitCounter>.Filter.And(
                Builders<MailRateLimitCounter>.Filter.Eq(x => x.LimiterKey, claim.LimiterKey),
                Builders<MailRateLimitCounter>.Filter.Eq(x => x.WindowStartUtc, claim.WindowStartUtc),
                Builders<MailRateLimitCounter>.Filter.Lte(x => x.Used, limit - cost));

            var now = DateTime.UtcNow;
            var update = Builders<MailRateLimitCounter>.Update
                .SetOnInsert(x => x.ItemId, $"{claim.LimiterKey}:{claim.WindowStartUtc.Ticks}")
                .SetOnInsert(x => x.LimiterKey, claim.LimiterKey)
                .SetOnInsert(x => x.WindowStartUtc, claim.WindowStartUtc)
                .SetOnInsert(x => x.WindowEndUtc, claim.WindowEndUtc)
                .SetOnInsert(x => x.CreatedAtUtc, now)
                .Set(x => x.UpdatedAtUtc, now)
                .Set(x => x.Limit, limit)
                .Inc(x => x.Used, cost);

            try
            {
                var counter = await collection.FindOneAndUpdateAsync(
                    filter,
                    update,
                    new FindOneAndUpdateOptions<MailRateLimitCounter>
                    {
                        IsUpsert = true,
                        ReturnDocument = ReturnDocument.After
                    });

                return new MailRateLimitCounterClaimResult
                {
                    IsAllowed = counter != null && counter.Used <= limit,
                    Used = counter?.Used ?? limit,
                    Limit = limit,
                    WindowEndUtc = claim.WindowEndUtc
                };
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return await GetRejectedRateLimitCounterClaimResultAsync(collection, claim, limit);
            }
            catch (MongoCommandException ex) when (ex.Code == 11000)
            {
                return await GetRejectedRateLimitCounterClaimResultAsync(collection, claim, limit);
            }
        }

        private static async Task<MailRateLimitCounterClaimResult> GetRejectedRateLimitCounterClaimResultAsync(
            IMongoCollection<MailRateLimitCounter> collection,
            MailRateLimitCounterClaim claim,
            int limit)
        {
            var existing = await collection
                .Find(x => x.LimiterKey == claim.LimiterKey && x.WindowStartUtc == claim.WindowStartUtc)
                .FirstOrDefaultAsync();

            return new MailRateLimitCounterClaimResult
            {
                IsAllowed = false,
                Used = existing?.Used ?? limit,
                Limit = existing?.Limit ?? limit,
                WindowEndUtc = existing?.WindowEndUtc ?? claim.WindowEndUtc
            };
        }

        private async Task EnsureRateLimitCounterIndexesAsync()
        {
            if (_rateLimitIndexesEnsured)
            {
                return;
            }

            await RateLimitIndexLock.WaitAsync();
            try
            {
                if (_rateLimitIndexesEnsured)
                {
                    return;
                }

                var collection = GetCollection<MailRateLimitCounter>();
                var indexes = new[]
                {
                    new CreateIndexModel<MailRateLimitCounter>(
                        Builders<MailRateLimitCounter>.IndexKeys
                            .Ascending(x => x.LimiterKey)
                            .Ascending(x => x.WindowStartUtc),
                        new CreateIndexOptions
                        {
                            Name = "ux_mail_rate_limit_counter_key_window",
                            Unique = true
                        }),
                    new CreateIndexModel<MailRateLimitCounter>(
                        Builders<MailRateLimitCounter>.IndexKeys.Ascending(x => x.WindowEndUtc),
                        new CreateIndexOptions
                        {
                            Name = "ttl_mail_rate_limit_counter_window_end",
                            ExpireAfter = TimeSpan.Zero
                        })
                };

                await collection.Indexes.CreateManyAsync(indexes);
                _rateLimitIndexesEnsured = true;
            }
            finally
            {
                RateLimitIndexLock.Release();
            }
        }

        public async Task<EmailSendQueryResult> GetEmailSendsAsync(GetEmailSends request, string tenantId)
        {
            var collection = GetCollection<MailToBeSent>();
            var builder = Builders<MailToBeSent>.Filter;
            var filter = builder.Eq(x => x.TenantId, tenantId);

            if (!string.IsNullOrWhiteSpace(request.OrganizationId))
            {
                filter &= builder.Eq(x => x.OrganizationId, request.OrganizationId.Trim());
            }

            if (request.SubmissionStatus.HasValue)
            {
                filter &= builder.Eq(x => x.SubmissionStatus, request.SubmissionStatus.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.Language))
            {
                filter &= builder.Eq(x => x.Language, request.Language.Trim());
            }

            if (request.CreatedFromUtc.HasValue)
            {
                filter &= builder.Gte(x => x.CreatedAtUtc, request.CreatedFromUtc.Value);
            }

            if (request.CreatedToUtc.HasValue)
            {
                filter &= builder.Lte(x => x.CreatedAtUtc, request.CreatedToUtc.Value);
            }

            if (request.SubmittedFromUtc.HasValue)
            {
                filter &= builder.Gte(x => x.SubmittedAtUtc, request.SubmittedFromUtc.Value);
            }

            if (request.SubmittedToUtc.HasValue)
            {
                filter &= builder.Lte(x => x.SubmittedAtUtc, request.SubmittedToUtc.Value);
            }

            filter &= BuildTextFilter(builder, request.Subject, nameof(MailToBeSent.Subject), nameof(MailToBeSent.TextSubject), "EmailTemplate.TemplateSubject");
            filter &= BuildTextFilter(builder, request.SenderAddress, nameof(MailToBeSent.SenderAddress), "MailServerConfiguration.SenderAddress");
            filter &= BuildTextFilter(
                builder,
                request.RecipientAddress,
                nameof(MailToBeSent.AllRecipients),
                nameof(MailToBeSent.To),
                nameof(MailToBeSent.Cc),
                nameof(MailToBeSent.Bcc),
                "RecipientDeliveryStatuses.Recipient");

            if (EmailSendContinuationToken.TryDecode(request.ContinuationToken, out var cursorCreatedAtUtc, out var cursorItemId))
            {
                filter &= builder.Or(
                    builder.Lt(x => x.CreatedAtUtc, cursorCreatedAtUtc),
                    builder.And(
                        builder.Eq(x => x.CreatedAtUtc, cursorCreatedAtUtc),
                        builder.Lt(x => x.ItemId, cursorItemId)));
            }

            var limit = request.PageSize + 1;
            var records = await collection
                .Find(filter)
                .Sort(Builders<MailToBeSent>.Sort.Descending(x => x.CreatedAtUtc).Descending(x => x.ItemId))
                .Limit(limit)
                .ToListAsync();

            return new EmailSendQueryResult
            {
                Items = records,
                HasMore = records.Count > request.PageSize
            };
        }

        private static FilterDefinition<MailToBeSent> BuildTextFilter(
            FilterDefinitionBuilder<MailToBeSent> builder,
            string? value,
            params string[] fields)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return builder.Empty;
            }

            var regex = new BsonRegularExpression(Regex.Escape(value.Trim()), "i");
            return builder.Or(fields.Select(field => builder.Regex(field, regex)));
        }

        //deprecated
        public async Task<(List<MailBoxEntity> Mails, long TotalCount)> GetMailBoxMails(GetMailBoxMails request)
        {
            var dbContext = _dbContextProvider.GetDatabase(request.ProjectKey);
            var collection = dbContext.GetCollection<MailBoxEntity>($"{nameof(MailBoxEntity)}s");

            var builder = Builders<MailBoxEntity>.Filter;
            var filter = builder.Empty;

            if (!string.IsNullOrEmpty(request.Status) && Enum.TryParse<MailStatus>(request.Status, true, out var status))
            {
                filter &= builder.Eq(x => x.Status, status);
            }

            if (!string.IsNullOrWhiteSpace(request.SendDateRange?.StartDate) && DateTime.TryParse(request.SendDateRange.StartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
            {
                filter &= builder.Gt(x => x.Date, startDate);
            }

            if (!string.IsNullOrWhiteSpace(request.SendDateRange?.EndDate) && DateTime.TryParse(request.SendDateRange.EndDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
            {
                filter &= builder.Lte(x => x.Date, endDate);
            }

            if (!string.IsNullOrEmpty(request.SearchText))
            {
                var searchFilter = builder.Or(
                    builder.Regex(x => x.Subject, new BsonRegularExpression(request.SearchText, "i")),
                    builder.Regex(x => x.From, new BsonRegularExpression(request.SearchText, "i")),
                    builder.Regex(x => x.To, new BsonRegularExpression(request.SearchText, "i"))
                );
                filter &= searchFilter;
            }

            if (request.IsInbound.HasValue)
            {
                filter &= builder.Eq(x => x.IsInbound, request.IsInbound.Value);
            }

            var totalCount = await collection.CountDocumentsAsync(filter);

            var mails = await collection.Find(filter)
                .SortByDescending(x => x.Date)
                .Skip(request.PageNumber * request.PageSize)
                .Limit(request.PageSize)
                .ToListAsync();

            return (mails, totalCount);
        }

        public async Task<(List<MailBoxEntityResponse> Mails, long TotalCount)> GetMailBoxAggregatedMails(GetMailBoxMails request)
        {
            var dbContext = _dbContextProvider.GetDatabase(request.ProjectKey);
            var collection = dbContext.GetCollection<MailBoxEntity>($"{nameof(MailBoxEntity)}s");

            var groupBy = new BsonDocument
                                        {
                                            { "_id", $"${nameof(MailBoxEntity.MessageId)}" },
                                            { nameof(MailBoxEntityResponse.Timeline),new BsonDocument{
                                                                { "$push",new BsonDocument{
                                                                    { nameof(MailBoxEntityResponse.Status),$"${nameof(MailBoxEntity.Status)}"},
                                                                    { nameof(MailBoxEntityResponse.Date),$"${nameof(MailBoxEntity.Date)}"}
                                                                }
                                                            }
                                                        }
                                            },
                                            { nameof(MailBoxEntityResponse.Status), new BsonDocument{{LastAccumulator, $"${nameof(MailBoxEntity.Status)}" }} },
                                            { nameof(MailBoxEntityResponse.ItemId), new BsonDocument{{LastAccumulator, $"$_id" }} },
                                            { nameof(MailBoxEntityResponse.Date), new BsonDocument{{LastAccumulator, $"${nameof(MailBoxEntity.Date)}" }} },
                                            { nameof(MailBoxEntityResponse.From), new BsonDocument{{LastAccumulator, $"${nameof(MailBoxEntity.From)}" }} },
                                            { nameof(MailBoxEntityResponse.To), new BsonDocument{{LastAccumulator, $"${nameof(MailBoxEntity.To)}" }} },
                                            { nameof(MailBoxEntityResponse.Subject), new BsonDocument{{LastAccumulator, $"${nameof(MailBoxEntity.Subject)}" }} },
                                            { nameof(MailBoxEntityResponse.Body), new BsonDocument{{LastAccumulator, $"${nameof(MailBoxEntity.Body)}" }} },
                                            { nameof(MailBoxEntityResponse.Error), new BsonDocument{{LastAccumulator, $"${nameof(MailBoxEntity.Error)}" }} },
                                            { nameof(MailBoxEntityResponse.RawMime), new BsonDocument{{LastAccumulator, $"${nameof(MailBoxEntity.RawMime)}" }} },
                                            { nameof(MailBoxEntityResponse.IsInbound), new BsonDocument{{LastAccumulator, $"${nameof(MailBoxEntity.IsInbound)}" }} },
                                        };

            var projection = new BsonDocument
                                                {
                                                    { "_id", $"${nameof(MailBoxEntityResponse.ItemId)}" },
                                                    { nameof(MailBoxEntity.MessageId), "$_id" },
                                                    { nameof(MailBoxEntityResponse.Timeline), 1 },
                                                    { nameof(MailBoxEntityResponse.Status), 1 },
                                                    { nameof(MailBoxEntityResponse.Date), 1 },
                                                    { nameof(MailBoxEntityResponse.From), 1 },
                                                    { nameof(MailBoxEntityResponse.To), 1 },
                                                    { nameof(MailBoxEntityResponse.Subject), 1 },
                                                    { nameof(MailBoxEntityResponse.Body), 1 },
                                                    { nameof(MailBoxEntityResponse.Error), 1 },
                                                    { nameof(MailBoxEntityResponse.RawMime), 1 },
                                                    { nameof(MailBoxEntityResponse.IsInbound), 1 },
                                                };

            var typeMatch = new BsonDocument();
            if (request.IsInbound.HasValue)
            {
                typeMatch.Add(nameof(MailBoxEntity.IsInbound), request.IsInbound.Value);
            }

            var match = new BsonDocument();
            if (!string.IsNullOrEmpty(request.Status) && Enum.TryParse<MailStatus>(request.Status, true, out var status))
            {
                match.Add(nameof(MailBoxEntityResponse.Status), status.ToString());
            }

            var dateFilter = new BsonDocument();
            if (!string.IsNullOrWhiteSpace(request.SendDateRange?.StartDate) && DateTime.TryParse(request.SendDateRange.StartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
                dateFilter.Add("$gt", startDate);
            if (!string.IsNullOrWhiteSpace(request.SendDateRange?.EndDate) && DateTime.TryParse(request.SendDateRange.EndDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
                dateFilter.Add("$lte", endDate);

            if (dateFilter.ElementCount > 0)
                match.Add(nameof(MailBoxEntityResponse.Date), dateFilter);

            if (!string.IsNullOrEmpty(request.SearchText))
            {
                var orConditions = new BsonArray
                        {
                            new BsonDocument(nameof(MailBoxEntityResponse.Subject), new BsonRegularExpression(request.SearchText, "i")),
                            new BsonDocument(nameof(MailBoxEntityResponse.From), new BsonRegularExpression(request.SearchText, "i")),
                            new BsonDocument(nameof(MailBoxEntityResponse.To), new BsonRegularExpression(request.SearchText, "i"))
                        };
                match.Add("$or", orConditions);
            }

            var baseAggregate = collection.Aggregate()
                .Match(typeMatch)
                .Sort(Builders<MailBoxEntity>.Sort.Ascending(x => x.Date)) 
                .Group(groupBy)
                .Match(match)
                .Sort(new BsonDocument(nameof(MailBoxEntityResponse.Date), -1));

            var countResult = await baseAggregate.Count().FirstOrDefaultAsync();
            var totalCount = countResult?.Count ?? 0;

            var mails = await baseAggregate
                .Project<MailBoxEntityResponse>(projection)
                .Skip(request.PageNumber * request.PageSize)
                .Limit(request.PageSize)
                .ToListAsync();

            return (mails, totalCount);
        }

        public async Task<MailBoxEntity> GetMailBoxMail(string messageId, string projectKey)
        {
            var dbContext = _dbContextProvider.GetDatabase(projectKey);
            var collection = dbContext.GetCollection<MailBoxEntity>($"{nameof(MailBoxEntity)}s");
            var filter = Builders<MailBoxEntity>.Filter.Eq(x => x.MessageId, messageId);

            var entities = await collection.Find(filter).ToListAsync();

            if (entities.Count == 0)
            {
                return null;
            }

            var latestEntity = entities
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Status != MailStatus.Sent)
                .First();

            if (string.IsNullOrEmpty(latestEntity.Body))
            {
                var sentEntity = entities.FirstOrDefault(x => x.Status == MailStatus.Sent);
                if (sentEntity != null && !string.IsNullOrEmpty(sentEntity.Body))
                {
                    latestEntity.Body = sentEntity.Body;
                }
            }

            return latestEntity;
        }
    }
}
