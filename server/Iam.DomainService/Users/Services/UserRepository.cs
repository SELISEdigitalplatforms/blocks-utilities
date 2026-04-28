using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Iam.DomainService.Users
{
    public class UserRepository : IUserRepository
    {
        private readonly IIdentityAccessManagementRepository _identityAccessManagementRepository;

        public UserRepository(IIdentityAccessManagementRepository identityAccessManagementRepository)
        {
            _identityAccessManagementRepository = identityAccessManagementRepository;
        }

        public async Task<bool> CheckPasswordBlackListedAsync(string password, string tenantId)
        {
            return await _identityAccessManagementRepository.CheckPasswordBlackListedAsync(password, tenantId);
        }

        public async Task<bool> CreateUserAsync(User user)
        {
            var collection = _identityAccessManagementRepository.GetCollection<User>();
            await collection.InsertOneAsync(user);

            return true;
        }

        public async Task<IamConfiguration> GetIamConfigurationAsync()
        {
            return await _identityAccessManagementRepository.GetIamConfigurationAsync();
        }

        public async Task<List<GetUserPermission>> GetPermissionsByResourcesAsync(string id)
        {
            var user = await _identityAccessManagementRepository.GetCollection<User>().Find(x => x.ItemId == id).FirstOrDefaultAsync();
            if (user == null || user.Memberships.Count == 0) return [];
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var project = Builders<Permission>.Projection.As<GetUserPermission>();
            var permissions = user.Memberships.SelectMany(m => m.Permissions);
            var filter = Builders<Permission>.Filter.In(x => x.Resource, permissions.ToList());
            return await collection.Find(filter).Project(project).ToListAsync();
        }

        public async Task<List<GetUserPermission>> GetPermissionsByResourcesAsync(List<string> permissions)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var project = Builders<Permission>.Projection.As<GetUserPermission>();
            var filter = Builders<Permission>.Filter.In(x => x.Resource, permissions);
            return await collection.Find(filter).Project(project).ToListAsync();
        }

        public async Task<List<GetUserRole>> GetRolesBySlugsAsync(string id)
        {
            var user = await _identityAccessManagementRepository.GetCollection<User>().Find(x => x.ItemId == id).FirstOrDefaultAsync();
            if (user == null || user.Memberships.Count == 0) return [];
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var project = Builders<Role>.Projection.As<GetUserRole>();
            var filter = Builders<Role>.Filter.In(x => x.Slug, GetOrgSpecficRoles(user));
            return await collection.Find(filter).Project(project).ToListAsync();
        }

        public async Task<List<GetUserRole>> GetRolesBySlugsAsync(List<string> roles)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var project = Builders<Role>.Projection.As<GetUserRole>();
            var filter = Builders<Role>.Filter.In(x => x.Slug, roles);
            return await collection.Find(filter).Project(project).ToListAsync();
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await _identityAccessManagementRepository.GetUserByEmailAsync(email);
        }

        public async Task<User> GetUserByIdAsync(string itemId)
        {
            return await _identityAccessManagementRepository.GetUserByIdAsync(itemId);
        }

        public async Task<T> GetUserByIdAsync<T>(string itemId)
        {
            return await _identityAccessManagementRepository.GetUserByIdAsync<T>(itemId);
        }

        public async Task<User> GetUserByUserNameOrgIdAsync(string userName, string organizatoinId = "")
        {
            var collection = _identityAccessManagementRepository.GetCollection<User>();

            var user = !string.IsNullOrWhiteSpace(organizatoinId)
                ? await collection.Find(x => x.UserName == userName && x.Memberships.Any(m => m.OrganizationId == organizatoinId)).FirstOrDefaultAsync()
                : await collection.Find(x => x.UserName == userName).FirstOrDefaultAsync();

            return user;
        }

        public async Task<(IQueryable<T>?, long)> GetUsersAsync<T, R>(R query) where R : BaseGetsRequest<GetUsersFilter>
        {
            var collection = _identityAccessManagementRepository.GetCollection<User>();

            var filter = BuildUserFilter(query.Filter);
            var sort = BuildSortDefinition(query.Sort);
            var projection = Builders<User>.Projection.As<T>();

            var totalCount = await collection.CountDocumentsAsync(filter);

            var options = new FindOptions<User, T>
            {
                Skip = query.PageSize * query.Page,
                Limit = query.PageSize,
                Sort = sort,
                Projection = projection
            };

            var cursor = await collection.FindAsync(filter, options);
            var data = await cursor.ToListAsync();

            return (data.AsQueryable(), totalCount);
        }

        private static FilterDefinition<User> BuildUserFilter(GetUsersFilter? filter)
        {
            var builder = Builders<User>.Filter;
            var filters = new List<FilterDefinition<User>>();

            if (filter == null) return builder.Empty;

            if (!string.IsNullOrWhiteSpace(filter.Name))
            {
                var searchTerm = filter.Name.Trim().ToLower();
                var regex = new BsonRegularExpression(searchTerm, "i");

                var orFilters = new List<FilterDefinition<User>>
                {
                  builder.Regex(u => u.FirstName, regex),
                  builder.Regex(u => u.LastName, regex),
                  builder.Where(u => (u.FirstName + " " + u.LastName).ToLower().Contains(searchTerm))
                };

                filters.Add(builder.Or(orFilters));
            }

            if (!string.IsNullOrWhiteSpace(filter.Email))
                filters.Add(builder.Eq(u => u.Email, filter.Email));

            if (filter.Status?.Active == true)
                filters.Add(builder.Eq(u => u.Active, true));

            if (filter.Status?.Inactive == true)
                filters.Add(builder.Eq(u => u.Active, false));

            if (filter.Mfa?.Enabled == true)
                filters.Add(builder.Eq(u => u.MfaEnabled, true));

            if (filter.Mfa?.Disabled == true)
                filters.Add(builder.Eq(u => u.MfaEnabled, false));

            if (filter.JoinedOn.HasValue)
                filters.Add(builder.Gte(u => u.CreatedDate, filter.JoinedOn.Value.Date));

            if (filter.LastLogin.HasValue)
                filters.Add(builder.Gte(u => u.LastLoggedInTime, filter.LastLogin.Value.Date));

            if (filter.UserIds is not null && filter.UserIds.Count > 0)
                filters.Add(builder.In("_id", filter.UserIds));

            if (!string.IsNullOrWhiteSpace(filter.OrganizationId))
                filters.Add(builder.AnyEq("OrganizationIds", filter.OrganizationId));

            return filters.Any() ? builder.And(filters) : builder.Empty;
        }

        private static SortDefinition<User> BuildSortDefinition(BaseSortRequest? sortRequest)
        {
            var builder = Builders<User>.Sort;

            if (sortRequest == null || string.IsNullOrWhiteSpace(sortRequest.Property))
                return builder.Descending(u => u.CreatedDate);

            return sortRequest.IsDescending
                ? builder.Descending(sortRequest.Property)
                : builder.Ascending(sortRequest.Property);
        }

        public async Task<bool> InsertUserKeyMapAsync(UserKeyMap userKeyMap)
        {
            return await _identityAccessManagementRepository.InsertUserKeyMapAsync(userKeyMap);
        }

        public async Task<bool> InsertUserTimelineAsync(UserTimeline userTimeline)
        {
            return await _identityAccessManagementRepository.InsertUserTimelineAsync(userTimeline);
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            return await _identityAccessManagementRepository.UpdateUserAsync(user);
        }

        public async Task<List<UserTimeline>> GetUserTimelinesAsync(GetUserTimeLineRequest request)
        {
            var collection = _identityAccessManagementRepository.GetCollection<UserTimeline>();
            var builder = Builders<UserTimeline>.Filter;
            var filter = FilterDefinition<UserTimeline>.Empty;

            if (!string.IsNullOrWhiteSpace(request?.Filter.Event))
                filter = builder.Eq(x => x.Event, request.Filter.Event);

            var options = new FindOptions<UserTimeline>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize
            };

            var userTimeLines = await collection.FindAsync(filter, options);
            return await userTimeLines.ToListAsync();
        }

        public async Task<string> GetProjectIdFromProjectPeopleAsync(string userId)
        {
            var collection = _identityAccessManagementRepository.GetCollection<ProjectPeople>();
            var filter = Builders<ProjectPeople>.Filter.Eq(x => x.UserId, userId);
            return (await collection.FindAsync(filter)).FirstOrDefault().TenantId;
        }

        private static List<string> GetOrgSpecficRoles(User user)
        {
            var orgId = BlocksContext.GetContext()?.OrganizationId;
            return user.Memberships.Where(m => m.OrganizationId == orgId).FirstOrDefault()?.Roles ?? [];
        }
    }
}
