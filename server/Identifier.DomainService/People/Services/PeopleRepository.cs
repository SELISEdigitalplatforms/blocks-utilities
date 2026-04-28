using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using DomainService.Shared.Entities;
using Iam.DomainService.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using Pipelines.Sockets.Unofficial.Arenas;

namespace DomainService.People
{
    public class PeopleRepository : IPeopleRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ITenants _tenants;
        private readonly IProjectRepository _projectRepository;

        private const string _userCollectionName = "Users";
        private const string _peopleCollectionName = "ProjectPeoples";

        public PeopleRepository(IDbContextProvider dbContextProvider, ITenants tenants, IProjectRepository projectRepository)
        {
            _dbContextProvider = dbContextProvider;
            _tenants = tenants;
            _projectRepository = projectRepository;
        }

        public async Task<(List<GetProjectPeople> peoples, long totalCount, long peoplesTotalCount, bool isOwner)> GetPeoplesAsync(GetPeoplesRequest request)
        {
            var peopleCollection = _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName);
            var userCollection = _dbContextProvider.GetCollection<User>(_userCollectionName);

            var projectIds = await _projectRepository.GetProjectIdsByGroupId(request.ProjectGroupId);
            FilterDefinition<ProjectPeople> projectPeopleFilter = Builders<ProjectPeople>.Filter.In("TenantId", projectIds);

            if (request.EnvironmentIds is { Count: > 0 })
            {
                projectPeopleFilter &= Builders<ProjectPeople>.Filter.In("TenantId", request.EnvironmentIds);
            }

            if (request.IsInvitationConfirmed.HasValue)
            {
                projectPeopleFilter &= Builders<ProjectPeople>.Filter.Eq(x => x.IsInvitationConfirmed, request.IsInvitationConfirmed.Value);
            }

            if (!string.IsNullOrWhiteSpace(request?.Filter))
            {
                var regex = new BsonRegularExpression(request.Filter.ToString(), "i");
                var userFilter = Builders<User>.Filter.Or(
                    Builders<User>.Filter.Regex(x => x.Email, regex),
                    Builders<User>.Filter.Regex(x => x.FirstName, regex),
                    Builders<User>.Filter.Regex(x => x.LastName, regex)
                );
                var matchingUserIds = await userCollection.Find(userFilter).Project(x => x.ItemId).ToListAsync();
                projectPeopleFilter &= Builders<ProjectPeople>.Filter.In(x => x.UserId, matchingUserIds);
            }

            var totalCount = await peopleCollection.CountDocumentsAsync(projectPeopleFilter);
            var peoplesTotalCount = await peopleCollection.Distinct(x => x.UserId, projectPeopleFilter).ToListAsync();

            var options = new FindOptions<ProjectPeople>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize
            };

            var peopleCursor = await peopleCollection.FindAsync(projectPeopleFilter, options);
            var projectPeoples = await peopleCursor.ToListAsync(); 

            var filter = Builders<User>.Filter.In(x => x.ItemId, projectPeoples.Select(x => x.UserId));
            var users = (await userCollection.Find(filter).ToListAsync()).ToDictionary(x => x.ItemId, x => x);

            var peoples = projectPeoples.Select(x =>
            {
                var projectPeople = new GetProjectPeople
                {
                    ItemId = x.ItemId,
                    peopleDetails = new PeopleDetails { UserId = x.UserId },
                    TenantId = x.TenantId,
                    IsInvitationSent = x.IsInvitationSent,
                    IsInvitationConfirmed = x.IsInvitationConfirmed,
                    IsCreator = x.IsCreator,
                    Enviroment = _tenants.GetTenantByID(x.TenantId)?.Environment ?? string.Empty,
                };

                var user = users.ContainsKey(x.UserId) ? users[x.UserId] : null; if (user != null)
                {
                    projectPeople.peopleDetails.Email = user.Email;
                    projectPeople.peopleDetails.FirstName = user.FirstName;
                    projectPeople.peopleDetails.LastName = user.LastName;
                    projectPeople.peopleDetails.Salutation = user.Salutation;
                    projectPeople.peopleDetails.ProfileImageUrl = user.ProfileImageUrl;
                    projectPeople.peopleDetails.AllowResendActivation = !user.Active || !user.IsVarified;
                }
                return projectPeople;
            });

            var isOwner = await IsOwner(BlocksContext.GetContext().UserId ?? "", projectIds);
            return (peoples.ToList(), totalCount, peoplesTotalCount.Count, isOwner);
        }


        public async Task<Tenant> GetProjectByIdAsync(string projectKey)
        {
            var filter = Builders<Tenant>.Filter.Eq(mc => mc.TenantId, projectKey);
            return await _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName).Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> IsPeoplesWithinLimit(InvitationDetails request, string resource)
        {
            var collection = _dbContextProvider.GetDatabase(request.ProjectKey).GetCollection<ResourceLimit>("ResourceLimits");
            var filter = Builders<ResourceLimit>.Filter.Eq(r => r.Resource, resource);
            var resourceLimit = await collection.Find(filter).FirstOrDefaultAsync();

            if (resourceLimit is not null && resourceLimit.Limit >= request.Emails.Count())
            {
                return true;
            }

            return false;
        }

        public async Task<List<User>> GetUsersByEmailAsync(List<string> emails)
        {
            var filter = Builders<User>.Filter.In(x => x.Email, emails);
            return await _dbContextProvider.GetCollection<User>(_userCollectionName).Find(filter).ToListAsync();
        }

        public async Task<User> GetUserByIdAsync(string userId)
        {
            var filter = Builders<User>.Filter.Eq(x => x.ItemId, userId);
            return await _dbContextProvider.GetCollection<User>(_userCollectionName).Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> InsertPeoplesAsync(List<ProjectPeople> projectPeoples)
        {
            await _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName).InsertManyAsync(projectPeoples);
            return true;
        }

        public async Task<bool> RemovePeoplesAsync(string email, List<string> projectKeys)
        {
            var filter = Builders<ProjectPeople>.Filter.Eq(x => x.Email, email)
                & Builders<ProjectPeople>.Filter.In(x => x.TenantId, projectKeys);
            var result = await _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName).DeleteManyAsync(filter);
            return result.IsAcknowledged;
        }

        public async Task<bool> UpdateProjectPeoples(List<string> ids)
        {
            var filter = Builders<ProjectPeople>.Filter.In(x => x.ItemId, ids);
            var update = Builders<ProjectPeople>.Update.Set(x => x.IsInvitationConfirmed, true);
            var result = await _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName).UpdateManyAsync(filter, update);
            return result.IsAcknowledged;
        }

        public async Task<List<ProjectPeople>> GetProjectPeoplesAsync(string userId, List<string> projectKeys)
        {
            var filter = Builders<ProjectPeople>.Filter.Eq(x => x.UserId, userId) & Builders<ProjectPeople>.Filter.In(x => x.TenantId, projectKeys);
            return await _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName).Find(filter).ToListAsync();
        }

        public async Task<ProjectPeople> GetProjectPeopleAsync(string id)
        {
            var filter = Builders<ProjectPeople>.Filter.Eq(x => x.ItemId, id);
            return await _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName).Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> IsOwner(string userId, List<string> projectKeys)
        {
            var filter = Builders<ProjectPeople>.Filter.Eq(x => x.UserId, userId) & Builders<ProjectPeople>.Filter.In(x => x.TenantId, projectKeys)
                & Builders<ProjectPeople>.Filter.Eq(x => x.IsCreator, true);

            var result = await _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName).CountDocumentsAsync(filter);

            return result > 0;
        }

        public async Task<bool> UpdateProjectPeopleOwnerShipAsync(List<string> ids, bool ownerShipStatus)
        {
            var filter = Builders<ProjectPeople>.Filter.In(x => x.ItemId, ids);
            var update = Builders<ProjectPeople>.Update.Set(x => x.IsCreator, ownerShipStatus)
                                                       .Set(x=>x.IsInvitationConfirmed, true)
                                                       .Set(x=>x.IsInvitationSent, true);
            var result = await _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName).UpdateManyAsync(filter, update);
            return result.IsAcknowledged;
        }

        public async Task<bool> UpdateProjectOwnerShipAsync(List<string> tenantIds, string userId)
        {
            var filter = Builders<Tenant>.Filter.In(x => x.TenantId, tenantIds);
            var update = Builders<Tenant>.Update.Set(x => x.CreatedBy, userId)
                                                       .Set(x => x.LastUpdatedBy, BlocksContext.GetContext()?.UserId)
                                                       .Set(x => x.LastUpdatedDate, DateTime.UtcNow);

            var result = await _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName).UpdateManyAsync(filter, update);
            return result.IsAcknowledged;
        }



        public async Task<ProjectPeople> GetProjectPeopleByTenantIdAndUserIdAsync(string tenantId, string userId)
        {
            var filter = Builders<ProjectPeople>.Filter.Eq(x => x.TenantId, tenantId) & Builders<ProjectPeople>.Filter.Eq(x => x.UserId, userId);
            return await _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName).Find(filter).FirstOrDefaultAsync();

        }

        public async Task<SignUpSetting> GetSignUpSettingAsync()
        {
            var collection = _dbContextProvider.GetCollection<SignUpSetting>($"{nameof(SignUpSetting)}s");
            return await collection.Find(_ => true).FirstOrDefaultAsync();
        }
    }
}
