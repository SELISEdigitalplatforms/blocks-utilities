using System.Net.WebSockets;
using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mail.DomainService.Template.Services
{
    public class TemplateRepository : ITemplateRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "EmailTemplates";

        public TemplateRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task SaveAsync(EmailTemplate template)
        {
            var dataBase = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
            var collection = dataBase.GetCollection<EmailTemplate>(_collectionName);
            var filter = Builders<EmailTemplate>.Filter.Eq(mt => mt.ItemId, template.ItemId);

            await collection.ReplaceOneAsync(filter, template, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<GetAllTemplatesResponse> GetsAsync(GetAllTemplates request)
        {
            var dataBase = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
            var collection = dataBase.GetCollection<EmailTemplate>(_collectionName);

            var filterBuilder = Builders<EmailTemplate>.Filter;
            var filter = filterBuilder.Empty;

            if (!string.IsNullOrWhiteSpace(request.SearchKey))
            {
                var regexFilter = filterBuilder.Or(
                    filterBuilder.Regex("Name", new BsonRegularExpression($".*{request.SearchKey}.*", "i")),
                    filterBuilder.Regex("TemplateSubject", new BsonRegularExpression($".*{request.SearchKey}.*", "i"))
                    );
                filter = filterBuilder.And(filter, regexFilter);
            }

            if (!string.IsNullOrWhiteSpace(request.MailConfigurationId))
            {
                var mailConfigFilter = filterBuilder.Eq("MailConfigurationId", request.MailConfigurationId);
                filter = filterBuilder.And(filter, mailConfigFilter);
            }

            if (!string.IsNullOrWhiteSpace(request.Language))
            {
                var languageFilter = filterBuilder.Eq("Language", request.Language);
                filter = filterBuilder.And(filter, languageFilter);
            }

            var collectionCount = (await collection.Find(filter).ToListAsync()).Count;

            var sort = !string.IsNullOrWhiteSpace(request.SortProperty) && request.IsDescending ?
                            Builders<EmailTemplate>.Sort.Descending(request.SortProperty)
                          : Builders<EmailTemplate>.Sort.Ascending(request.SortProperty ?? "Name");

            var project = Builders<EmailTemplate>.Projection.As<EmailTemplate>();
            var findOptions = new FindOptions<EmailTemplate>
            {
                Limit = request.PageSize,
                Skip = request.PageSize * request.PageNumber,
                Sort = sort,
                Projection = project
            };

            var cursor = await collection.FindAsync(filter, findOptions);
            var templates = await cursor.ToListAsync();

            return new GetAllTemplatesResponse
            {
                Templates = templates,
                TotalCount = collectionCount
            };
        }

        public async Task<EmailTemplate> GetByIdAsync(string itemId)
        {
            var dataBase = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
            var collection = dataBase.GetCollection<EmailTemplate>(_collectionName);
            var filter = Builders<EmailTemplate>.Filter.Eq(mt => mt.ItemId, itemId);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<EmailTemplate> GetByNameAndLanguageAsync(string name, string language)
        {
            var dataBase = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
            var collection = dataBase.GetCollection<EmailTemplate>(_collectionName);
            var filter = Builders<EmailTemplate>.Filter.And(
                Builders<EmailTemplate>.Filter.Eq(mt => mt.Name, name),
                Builders<EmailTemplate>.Filter.Eq(mt => mt.Language, language)
            );

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task DeleteAsync(string itemId)
        {
            var dataBase = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId);
            var collection = dataBase.GetCollection<EmailTemplate>(_collectionName);
            var filter = Builders<EmailTemplate>.Filter.Eq(mc => mc.ItemId, itemId);

            await collection.DeleteOneAsync(filter);
        }
    }
}
