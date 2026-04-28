using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Iam.DomainService.Resources
{
    public class ResourceRepository : IResourceRepository
    {
        private readonly IIdentityAccessManagementRepository _identityAccessManagementRepository;

        public ResourceRepository(IIdentityAccessManagementRepository identityAccessManagementRepository)
        {
            _identityAccessManagementRepository = identityAccessManagementRepository;
        }

        public async Task<Permission> GetPermissionByResourceAsync(string resource)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.Eq(x => x.Resource, resource);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<Permission> GetPermissionByIdAsync(string id)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.Eq(x => x.ItemId, id);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> InsertPermissionAsync(Permission permission)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            await collection.InsertOneAsync(permission);
            return true;
        }

        public async Task<bool> UpdatePermissionAsync(Permission permission)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == permission.ItemId, permission);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<Role> GetRoleByIdAsync(string id)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var filter = Builders<Role>.Filter.Eq(x => x.ItemId, id);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> InsertRoleAsync(Role role)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            await collection.InsertOneAsync(role);
            return true;
        }

        public async Task<bool> UpdateRoleAsync(Role role)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == role.ItemId && x.Slug == role.Slug, role);

            return result?.IsAcknowledged ?? false;
        }

        public async Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverityAsync()
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var permissionCursor = await collection.FindAsync(Builders<Permission>.Filter.Empty);
            var severityGroups = permissionCursor.ToList().GroupBy(p => p.PermissionSeverity)
                .Select(g => new PermissionGroupBySeverityResponse
                {
                    SeverityLevel =  g.Key.ToString(),
                    Count = g.Count()
                }).ToList();

            return severityGroups;
        }

        public async Task<(IQueryable<Permission>, long)> GetPermissionsAsync(GetPermissionsRequest query)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = query.Roles.Count != 0
                ? Builders<Permission>.Filter.AnyIn(x => x.Roles, query.Roles)
                : Builders<Permission>.Filter.Empty;

            SortDefinition<Permission>? sort = null;

            if (query.Filter != null)
            {
                filter &= Builders<Permission>.Filter.Eq(x => x.IsArchived, query.Filter.IsArchived);
                if (query.Filter.Type != ResourceType.None)
                {
                    filter &= Builders<Permission>.Filter.Eq(x => x.Type, query.Filter.Type);
                }

                if (query.Filter.PermissionSeverity != PermissionSeverity.None)
                {
                    filter &= Builders<Permission>.Filter.Eq(x => x.PermissionSeverity, query.Filter.PermissionSeverity);
                }

                if (!string.IsNullOrWhiteSpace(query.Filter.Search))
                {
                    filter &= Builders<Permission>.Filter.Regex(x => x.Name, query.Filter.Search)
                        | Builders<Permission>.Filter.Regex(x => x.Resource, query.Filter.Search)
                        | Builders<Permission>.Filter.Regex(x => x.Description, query.Filter.Search);
                }

                if (!string.IsNullOrWhiteSpace(query.Filter.IsBuiltIn))
                {
                    filter &= Builders<Permission>.Filter.Eq(x => x.IsBuiltIn, query.Filter.IsBuiltIn.ToLower() == "yes");
                }

                if (query.Filter.Tags.Count > 0)
                {
                    filter &= Builders<Permission>.Filter.AnyIn(x => x.Tags, query.Filter.Tags);
                }

                if (!string.IsNullOrWhiteSpace(query.Filter.ResourceGroup))
                {
                    filter &= Builders<Permission>.Filter.Eq(x => x.ResourceGroup, query.Filter.ResourceGroup);
                }
                if (query.Filter.Resources.Count > 0)
                {
                    filter &= Builders<Permission>.Filter.In(x => x.Resource, query.Filter.Resources);
                }
            }

            if (query.Sort != null)
            {
                sort = query.Sort.IsDescending ? Builders<Permission>.Sort.Descending(query.Sort.Property) : Builders<Permission>.Sort.Ascending(query.Sort.Property);
            }

            var count = await collection.CountDocumentsAsync(filter);
            var permissions = await collection.Find(filter).Sort(sort).Limit(query.PageSize).Skip(query.PageSize * query.Page).ToListAsync();

            return (permissions.AsQueryable(), count);
        }

        public async Task<ResourceTimeline<T>> GetResourceTimelineAsync<T>(string itemId)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<ResourceTimeline<T>>("ResourceTimelines");
            var result = await collection.Find(x => x.ItemId == itemId).FirstOrDefaultAsync();

            return result;
        }

        public async Task<bool> SaveResourceTimelineAsync<T>(ResourceTimeline<T> timeline)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<ResourceTimeline<T>>("ResourceTimelines");
            var result = await collection.ReplaceOneAsync(x => x.ItemId == timeline.ItemId, timeline, new ReplaceOptions { IsUpsert = true });

            return result.IsAcknowledged;
        }

        public async Task<bool> SaveResourceTimelinesAsync<T>(List<ResourceTimeline<T>> timelines)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<ResourceTimeline<T>>("ResourceTimelines");
            await collection.InsertManyAsync(timelines);

            return true;
        }

        public async Task<Role> GetRoleBySlugAsync(string slug)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var filter = Builders<Role>.Filter.Eq(x => x.Slug, slug);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<(IQueryable<Role>, long)> GetRolesAsync(GetRolesRequest query)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Role>();
            var filter = Builders<Role>.Filter.Empty;
            SortDefinition<Role>? sort = null;

            if (query.Filter is not null)
            {
                if (!string.IsNullOrWhiteSpace(query.Filter.Search))
                {
                    var regex = new BsonRegularExpression(query.Filter.Search, "i");
                    filter &= Builders<Role>.Filter.Regex(x => x.Name, regex)
                        | Builders<Role>.Filter.Regex(x => x.Description, regex);
                }
                if (query.Filter.Slugs is not null && query.Filter.Slugs.Count > 0)
                    filter &= Builders<Role>.Filter.In(x => x.Slug, query.Filter.Slugs);

            }

            if (query.Sort != null)
            {
                sort = query.Sort.IsDescending ? Builders<Role>.Sort.Descending(query.Sort.Property) :
                                                 Builders<Role>.Sort.Ascending(query.Sort.Property);
            }

            var count = await collection.CountDocumentsAsync(filter);
            var roles = await collection.Find(filter).Sort(sort).Limit(query.PageSize).Skip(query.PageSize * query.Page).ToListAsync();

            return (roles.AsQueryable(), count);
        }

        public async Task<bool> UpdateRolePermissionByIdsAsync(string slug, List<string> permissions)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.In(x => x.ItemId, permissions);
            var update = Builders<Permission>.Update.AddToSet(x => x.Roles, slug);
            var result = await collection.UpdateManyAsync(filter, update);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<bool> RemoveRolePermissionByIdsAsync(string slug, List<string> permissions)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var filter = Builders<Permission>.Filter.In(x => x.ItemId, permissions);
            var update = Builders<Permission>.Update.Pull(x => x.Roles, slug);
            var result = await collection.UpdateManyAsync(filter, update);
            return result?.IsAcknowledged ?? false;
        }

        public async Task<bool> UpdateRolesCountAsync(string slug)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            var count = await collection.CountDocumentsAsync(x => x.Roles.Contains(slug));

            var update = Builders<Role>.Update.Set(x => x.Count, count);
            var result = await _identityAccessManagementRepository.GetCollection<Role>()
                .UpdateOneAsync(x => x.Slug == slug, update);

            return result?.IsAcknowledged ?? false;
        }

        public async Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync()
        {
            var collection = _identityAccessManagementRepository.GetCollection<Permission>();
            return (collection.AsQueryable()
                .Where(p => p.ResourceGroup != null)
                .GroupBy(p => p.ResourceGroup)
                .Select(g => new GetResourceGroupResponse
                {
                    ResourceGroup = g.Key,
                    Count = g.Count()
                })
                .ToList());
        }

        public async Task<Organization> GetOrganizationById(string resourceId)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            return await  (await collection.FindAsync(Builders<Organization>.Filter.Where(r=>r.ItemId == resourceId))).FirstOrDefaultAsync();
        }

        public async Task SaveOrganizationAsync(Organization organization)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            await collection.ReplaceOneAsync(r=>r.ItemId == organization.ItemId, organization, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request)
        {
            var collection = _identityAccessManagementRepository.GetCollection<Organization>();
            var filter = Builders<Organization>.Filter.Empty;

            var totalCount = await collection.CountDocumentsAsync(filter);

            var options = new FindOptions<Organization>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize
            };

            var organizations = await (await collection.FindAsync(filter, options)).ToListAsync();

            return new GetOrganizationsResponse { IsSuccess = true, Organizations = organizations, TotalCount = totalCount };
        }

        public async Task SaveOrganizationConfig(OrganizationConfig config)
        {
            var collection = _identityAccessManagementRepository.GetCollection<OrganizationConfig>();
            await collection.ReplaceOneAsync(r => r.ItemId == config.ItemId, config, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<OrganizationConfig> GetOrgConfigByIdAsync(string organizationId)
        {
            var collection = _identityAccessManagementRepository.GetCollection<OrganizationConfig>();
            return  await (await collection.FindAsync(org=>org.ItemId == organizationId)).FirstOrDefaultAsync();
        }

        public async Task<OrganizationConfig> GetOrganizationConfigAsync()
        {
            var collection = _identityAccessManagementRepository.GetCollection<OrganizationConfig>();
            return await (await collection.FindAsync(_=>true)).FirstOrDefaultAsync();
        }

        
    }
}
