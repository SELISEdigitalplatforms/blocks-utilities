using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Globalization;

namespace Mail.DomainService.Services
{
    public class MailRepository : IMailRepository
    {
        private const string LastAccumulator = "$last";
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

        public async Task<MailToBeSent> GetMailToBeSent(string itemId)
        {
            var collection = GetCollection<MailToBeSent>();

            var result = await collection.Find(x => x.ItemId == itemId).FirstOrDefaultAsync();
            return result;
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