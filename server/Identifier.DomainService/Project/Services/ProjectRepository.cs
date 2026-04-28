using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Shared;
using DomainService.Shared.Entities;
using DomainService.Shared.Services;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Pipelines.Sockets.Unofficial.Arenas;

namespace DomainService.Projects
{
    public class ProjectRepository : IProjectRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;
        private readonly IConfiguration _configuration;
        private readonly IEncodingService _urlEncodingService;

        public ProjectRepository(IDbContextProvider dbContextProvider,
                                 IConfiguration configuration,
                                 IBlocksSecret blocksSecret,
                                 IEncodingService urlEncodingService)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _configuration = configuration;
            _urlEncodingService = urlEncodingService;
        }

        public async Task<Tenant> GetByIdAsync(string itemId)
        {
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Tenant>.Filter.Eq(mc => mc.ItemId, itemId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<Tenant>> GetByGroupIdAsync(string tenantGroupId)
        {
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Tenant>.Filter.Eq(mc => mc.TenantGroupId, tenantGroupId);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<Tenant> GetByDomainAsync(string name)
        {
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Tenant>.Filter.Eq(mc => mc.ApplicationDomain, name);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task InsertProjectAsync(Tenant project)
        {
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

            await collection.InsertOneAsync(project);
        }

        public async Task UpdateTenantAssetAsync(TenantAsset asset)
        {
            var collection = _dbContextProvider.GetCollection<TenantAsset>(IdentifierConstants.TenantAssetCollectionName);
            await collection.InsertOneAsync(asset);
        }

        public async Task<(TenantAsset? assets, long totalCount)> GetTenantAssetAsync(GetAssetRequest request)
        {
            var sharedProjects = await GetProjectPeoplesAsync(request.TenantGroupId);
            if (sharedProjects == null || sharedProjects.Count == 0)
            {
                return (null, 0);
            }

            var collection = _dbContextProvider.GetCollection<TenantAsset>(IdentifierConstants.TenantAssetCollectionName);
            var documentFilter = Builders<TenantAsset>.Filter.Eq(mc => mc.TenantGroupId, request.TenantGroupId);
            var tenantAsset = await collection.Find(documentFilter).FirstOrDefaultAsync();

            if (tenantAsset == null)
                return (null, 0);

            var filteredResources = tenantAsset.Resources?.AsEnumerable() ?? Enumerable.Empty<Resource>();

            if (request.Filter != null)
            {
                if (!string.IsNullOrWhiteSpace(request.Filter.Name))
                {
                    filteredResources = filteredResources.Where(r =>
                        r.Name != null && r.Name.Contains(request.Filter.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(request.Filter.Link))
                {
                    filteredResources = filteredResources.Where(r =>
                        r.Link != null && r.Link.Contains(request.Filter.Link.Trim(), StringComparison.OrdinalIgnoreCase));
                }
            }

            var totalCount = filteredResources.Count();

            var pagedResources = filteredResources
                .Skip(request.PageSize * request.Page)
                .Take(request.PageSize)
                .ToList();

            tenantAsset.Resources = pagedResources;
            return (tenantAsset, totalCount);
        }

        public async Task UpdateProjectAsync(Tenant project)
        {
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Tenant>.Filter.Eq(mc => mc.ItemId, project.ItemId);

            await collection.ReplaceOneAsync(filter, project);
        }

        public async Task<List<GroupedProjectsDto>> GetAllByLastModifiedDateAsync(GetProjectsRequest request)
        {
            var collection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);

            var filter = !string.IsNullOrEmpty(request.TenantGroupId) ?

                          Builders<Project>.Filter.And(Builders<Project>.Filter.Eq(mc => mc.CreatedBy, BlocksContext.GetContext()?.UserId),
                                                       Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false),
                                                       Builders<Project>.Filter.Eq(mc => mc.TenantGroupId, request.TenantGroupId)) :

                          Builders<Project>.Filter.And(Builders<Project>.Filter.Eq(mc => mc.CreatedBy, BlocksContext.GetContext()?.UserId),
                                                       Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false));

            var option = new FindOptions<Project>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize,
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            };

            using var cursor = await collection.FindAsync(filter, option);
            var selfProjects = await cursor.ToListAsync();
            var selfGroupedProjectsTasks = selfProjects.GroupBy(p => p.TenantGroupId ?? string.Empty)
                                               .Select(g => new GroupedProjectsDto
                                               {
                                                   TenantGroupId = g.Key,
                                                   Projects = g.OrderByDescending(p => p.LastUpdatedBy).ToList(),
                                                   IsShared = false,
                                                   NonSharedProject = []
                                               }).ToList();


            var sharedProjects = await GetSharedProjectsAsync(request.TenantGroupId);

            var sharedGroupProjects = sharedProjects.GroupBy(p => p.TenantGroupId ?? string.Empty)
                                               .Select(async g => new GroupedProjectsDto
                                               {
                                                   TenantGroupId = g.Key,
                                                   Projects = g.OrderByDescending(p => p.LastUpdatedBy).ToList(),
                                                   IsShared = true,
                                                   NonSharedProject = await GetNosharedProjectsAsync(sharedProjects, g.Key)
                                               }).ToList();

            var groupedSharedProject = (await Task.WhenAll(sharedGroupProjects)).ToList();
            return [.. selfGroupedProjectsTasks, .. groupedSharedProject];
        }

        private async Task<List<Project>> GetNosharedProjectsAsync(List<Project> sharedProjects, string tenantGroupId)
        {
            var projectCollection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.Nin(p => p.TenantId, sharedProjects?.Select(doc => doc?.TenantId)) &
                         Builders<Project>.Filter.Where(p => p.IsDisabled == false) &
                         Builders<Project>.Filter.Where(p => p.TenantGroupId == tenantGroupId);

            using var projectCursor = await projectCollection.FindAsync(filter, new FindOptions<Project>
            {
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            });

            return await projectCursor.ToListAsync();
        }

        public async Task<List<Project>> GetSharedProjectsAsync(string? tenantGroupId = null)
        {
            var projectPeopleCollection = _dbContextProvider.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName);

            var projectPeopleFilter = Builders<ProjectPeople>.Filter.And(
                Builders<ProjectPeople>.Filter.Eq(mc => mc.UserId, BlocksContext.GetContext()?.UserId),
                Builders<ProjectPeople>.Filter.Or(
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsInvitationConfirmed, true),
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsCreator, true)));

            var documentsCursor = await projectPeopleCollection.FindAsync(projectPeopleFilter);
            var documents = await documentsCursor.ToListAsync();

            var projectCollection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.In(p => p.TenantId, documents?.Select(doc => doc?.TenantId)) &
                         Builders<Project>.Filter.Where(p => p.IsDisabled == false) &
                         Builders<Project>.Filter.Ne(p => p.CreatedBy, BlocksContext.GetContext().UserId);

            if (!string.IsNullOrEmpty(tenantGroupId))
            {
                filter &= Builders<Project>.Filter.Eq(p => p.TenantGroupId, tenantGroupId);
            }

            using var projectCursor = await projectCollection.FindAsync(filter, new FindOptions<Project>
            {
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            });

            return await projectCursor.ToListAsync();
        }

        public async Task<List<Project>> GetProjectPeoplesAsync(string tenantGroupId)
        {
            var projectPeopleCollection = _dbContextProvider.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName);

            var projectPeopleFilter = Builders<ProjectPeople>.Filter.And(
                Builders<ProjectPeople>.Filter.Eq(mc => mc.UserId, BlocksContext.GetContext()?.UserId),
                Builders<ProjectPeople>.Filter.Or(
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsInvitationConfirmed, true),
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsCreator, true)));

            var documentsCursor = await projectPeopleCollection.FindAsync(projectPeopleFilter);
            var documents = await documentsCursor.ToListAsync();

            var projectCollection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.In(p => p.TenantId, documents?.Select(doc => doc?.TenantId)) &
                         Builders<Project>.Filter.Where(p => p.IsDisabled == false);

            filter &= Builders<Project>.Filter.Eq(p => p.TenantGroupId, tenantGroupId);

            using var projectCursor = await projectCollection.FindAsync(filter, new FindOptions<Project>
            {
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            });

            return await projectCursor.ToListAsync();
        }

        public async Task SaveStatusTracerAsync(ProjectStatusTracer statusTrace)
        {
            var collection = _dbContextProvider.GetCollection<ProjectStatusTracer>(IdentifierConstants.ProjectStatusTracerCollectionName);

            var filter = Builders<ProjectStatusTracer>.Filter.Eq(tracer => tracer.ProjectId, statusTrace.ProjectId);
            await collection.ReplaceOneAsync(filter, statusTrace, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<List<ProjectStatusTracer>> GetAllUnfinishedProjectAsync()
        {
            var collection = _dbContextProvider.GetCollection<ProjectStatusTracer>(IdentifierConstants.ProjectStatusTracerCollectionName);

            var filter = Builders<ProjectStatusTracer>.Filter.Eq(mc => mc.IsProjectCreationSuccess, false);
            var unfinishedList = await collection.FindAsync(filter);
            return await unfinishedList.ToListAsync();
        }

        public async Task CreateDefaultConfigurationAsync(ProjectStatusTracer statusTracer, Tenant project)
        {
            if (statusTracer.IsDefaultConfigurationCopied) return;

            var dataBase = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, $"{project.DBName}");

            await InitializeDefaultConfigurationsAsync(dataBase, project);
            statusTracer.IsDefaultConfigurationCopied = true;
        }

        private async Task InitializeDefaultConfigurationsAsync(IMongoDatabase consumerDb, Tenant project)
        {
            var sourceDatabase = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, "BlocksConfiguration");

            await Task.WhenAll(
                CopyDocumentAsync(sourceDatabase, consumerDb, "MailServerConfigurations", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "EmailTemplates", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "StorageConfigurations", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "BlocksLanguages", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "AuthenticationConfigurations", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "UilmFiles", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "BlocksLanguageModules", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "BlocksLanguageKeys", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "Roles", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "Permissions", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "Organizations", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "SchemaDefinitions", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "SignUpSettings", project.TenantId),
                CopyAndCustomizeIamConfigurationAsync(sourceDatabase, consumerDb, project),
                // CopyAndCustomizeResourceLimitsAsync(sourceDatabase, consumerDb, project),
                CopyDocumentAsync(sourceDatabase, consumerDb, "LinkBasedActionConfigs", project.TenantId),
                CopyDocumentAsync(sourceDatabase, consumerDb, "DmsArtifacts", project.TenantId));
        }

        private async Task CopyDocumentAsync(IMongoDatabase sourceDb, IMongoDatabase targetDb, string collectionName, string tenantId)
        {
            var sourceCollection = sourceDb.GetCollection<BsonDocument>(collectionName);
            var documents = await (await sourceCollection.FindAsync(_ => true)).ToListAsync();
            var userId = BlocksContext.GetContext()?.UserId;

            foreach (var document in documents)
            {
                document["CreatedBy"] = userId;
                document["LastUpdatedBy"] = userId;
                var targetCollection = targetDb.GetCollection<BsonDocument>(collectionName);
                await targetCollection.InsertOneAsync(document);
            }

        }

        private async Task CopyAndCustomizeIamConfigurationAsync(IMongoDatabase sourceDb, IMongoDatabase targetDb, Tenant project)
        {
            var sourceCollection = sourceDb.GetCollection<BsonDocument>("IamConfigurations");
            var iamConfiguration = await sourceCollection.Find(_ => true).FirstOrDefaultAsync();
            var userId = BlocksContext.GetContext()?.UserId;

            if (iamConfiguration != null)
            {
                iamConfiguration["AccountActivationUrl"] = $"{project.ApplicationDomain}/activate";
                iamConfiguration["AccountVerificationUrl"] = $"{project.ApplicationDomain}/verify";
                iamConfiguration["RecoverAccountUrl"] = $"{project.ApplicationDomain}/resetpassword";
                iamConfiguration["CreatedBy"] = userId;
                iamConfiguration["LastUpdatedBy"] = userId;

                var targetCollection = targetDb.GetCollection<BsonDocument>("IamConfigurations");
                await targetCollection.InsertOneAsync(iamConfiguration);
            }
        }

        private async Task CopyAndCustomizeResourceLimitsAsync(IMongoDatabase sourceDb, IMongoDatabase targetDb, Tenant project)
        {
            var sourceCollection = sourceDb.GetCollection<BsonDocument>("ResourceLimits");
            var resourceLimits = await (await sourceCollection.FindAsync(_ => true)).ToListAsync();
            var userId = BlocksContext.GetContext()?.UserId;

            foreach (var resourceLimit in resourceLimits)
            {
                resourceLimit["CreatedBy"] = userId;
                resourceLimit["LastUpdatedBy"] = userId;
                resourceLimit["CreatedDate"] = DateTime.UtcNow;
                resourceLimit["LastUpdatedDate"] = DateTime.UtcNow;
                resourceLimit["Language"] = "en-US";
                resourceLimit["TenantId"] = project.TenantId;
                resourceLimit["Lifetime"] = DateTime.UtcNow.AddMonths(1);
                resourceLimit["Language"] = "en-US";

                var targetCollection = targetDb.GetCollection<BsonDocument>("ResourceLimits");
                await targetCollection.InsertOneAsync(resourceLimit);
            }
        }

        public async Task UpdateIamConfiguration(Tenant project)
        {
            var targetedDb = _dbContextProvider.GetDatabase(project.TenantId);
            var collection = targetedDb.GetCollection<BsonDocument>("IamConfigurations");

            var iamConfiguration = await collection.Find(_ => true).FirstOrDefaultAsync();

            if (iamConfiguration != null)
            {
                var filter = Builders<BsonDocument>.Filter.Eq("_id", iamConfiguration["_id"]);

                var update = Builders<BsonDocument>.Update
                    .Set("AccountActivationUrl", $"{project.ApplicationDomain}/activate")
                    .Set("AccountVerificationUrl", $"{project.ApplicationDomain}/verify")
                    .Set("RecoverAccountUrl", $"{project.ApplicationDomain}/resetpassword")
                    .Set("CreatedBy", project.TenantId)
                    .Set("LastUpdatedBy", project.TenantId);

                await collection.UpdateOneAsync(filter, update);
            }
        }


        public async Task SaveRepoInfoAsync(Tenant project, List<Resource>? resources)
        {
            var targetDb = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, $"{project.DBName}");
            var tenantSlug = await _urlEncodingService.EncodeToBase26Async(project.TenantGroupId, project.TenantGroupId, 5);

            List<BsonDocument> documents = [];
            foreach (var resource in resources)
            {
                var repoSlug = await _urlEncodingService.EncodeToBase26Async(resource.ResourceId, project.TenantGroupId, 5);
                documents.Add(GetRepoObject(resource, project, tenantSlug, repoSlug));
            }

            if (documents.Count > 0)
            {
                documents[0]["DefaultDeploymentUrl"] = project.ApplicationDomain;
                var targetCollection = targetDb.GetCollection<BsonDocument>("Repos");
                await targetCollection.InsertManyAsync(documents);
            }
        }

        public async Task UpdateRepoResourceAsync(AddAssetRequest request)
        {
            var projects = await GetByGroupIdAsync(request.TenantGroupId);
            var repoSlug = await _urlEncodingService.EncodeToBase26Async(request.Resource.ResourceId, request.TenantGroupId, 5);

            foreach (var project in projects)
            {
                var tenantSlug = await _urlEncodingService.EncodeToBase26Async(project.TenantGroupId, request.TenantGroupId, 5);
                var targetedDb = _dbContextProvider.GetDatabase(project.TenantId);
                var reposCollection = targetedDb.GetCollection<BsonDocument>("Repos");
                await reposCollection.InsertOneAsync(GetRepoObject(request.Resource, project, tenantSlug, repoSlug));
            }
        }

        private BsonDocument GetRepoObject(Resource resource, Tenant project, string tenantSlug, string repoSlug)
        {
            string deployDomain = $"https://{IdentifierHelper.EnvironmentMapper(project.Environment)}{tenantSlug}-{repoSlug}{_configuration["KbtclIdentifier"]}";

            return new BsonDocument
            {
                ["_id"] = Guid.NewGuid().ToString(),
                ["SourceRepoId"] = resource.ResourceId,
                ["RepoName"] = resource.Name,
                ["RepoUrl"] = resource.Link,
                ["CreatedDate"] = DateTime.UtcNow,
                ["LastUpdatedDate"] = DateTime.UtcNow,
                ["CreatedBy"] = BlocksContext.GetContext().UserId,
                ["Branch"] = project.Environment == "prod" ? "main" : project.Environment,
                ["ProjectId"] = project.TenantId,
                ["ProjectName"] = project.Name,
                ["DeploymentType"] = "Manual",
                ["DefaultDeploymentUrl"] = deployDomain.ToLower()
            };
        }

        public async Task<long> GetProjectCountAsync()
        {
            var collection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Project>.Filter.And(Builders<Project>.Filter.Eq(mc => mc.CreatedBy, BlocksContext.GetContext()?.UserId),
                                                      Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false));

            return await collection.CountDocumentsAsync(filter);
        }

        public async Task<bool> IsExistingEnviroment(List<string> enviroments, string tenantGroupId)
        {
            var collection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.And(Builders<Project>.Filter.In(mc => mc.Environment, enviroments),
                                                      Builders<Project>.Filter.Eq(mc => mc.TenantGroupId, tenantGroupId),
                                                      Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false));
            var count = await collection.CountDocumentsAsync(filter);
            return count > 0;
        }

        public async Task InsertPeopleAsync(ProjectPeople projectPeople)
        {
            await _dbContextProvider.GetCollection<ProjectPeople>("ProjectPeoples").InsertOneAsync(projectPeople);
        }

        public async Task<bool> SaveTenantCertificate(TenantCertificate tenantCertificate)
        {
            await _dbContextProvider.GetCollection<TenantCertificate>("TenantCertificates")
                .ReplaceOneAsync(x => x.ItemId == tenantCertificate.ItemId, tenantCertificate, new ReplaceOptions { IsUpsert = true });

            return true;
        }

        public async Task<Tenant> GetByTenantIdAsync(string tenantId)
        {
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Tenant>.Filter.Eq(mc => mc.TenantId, tenantId);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<List<SsoInfo>> GetSsoInfoAsync()
        {
            var collection = _dbContextProvider.GetCollection<SsoInfo>("SocialLoginCredentials");

            var filter = Builders<SsoInfo>.Filter.Eq(mc => mc.IsDisabled, false);
            return await (await collection.FindAsync(filter)).ToListAsync();
        }

        public async Task SaveTenantAssetAsync(TenantAsset asset)
        {
            var collection = _dbContextProvider.GetCollection<TenantAsset>(IdentifierConstants.TenantAssetCollectionName);
            var filter = Builders<TenantAsset>.Filter.Eq(mc => mc.TenantGroupId, asset.TenantGroupId);
            await collection.ReplaceOneAsync(filter, asset, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<BlocksGuid> GetBlocksGuidAsync(string tenantGroupId)
        {
            var collection = _dbContextProvider.GetCollection<BlocksGuid>($"{nameof(BlocksGuid)}s");
            var filter = Builders<BlocksGuid>.Filter.Eq(mc => mc.TenantGroupId, tenantGroupId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<BaseResponse> SaveJWTClaimsAsync(ThirdPartyJWTClaims mapper)
        {
            var collection = _dbContextProvider.GetCollection<ThirdPartyJWTClaims>("ThirdPartyJWTClaims");
            var filter = Builders<ThirdPartyJWTClaims>.Filter.Eq(m => m.ItemId, mapper.ItemId);
            await collection.ReplaceOneAsync(filter, mapper, new ReplaceOptions { IsUpsert = true });

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<ThirdPartyJWTClaims> GetThirdPartyJWTClaimsAsync(string itemId)
        {
            var collection = _dbContextProvider.GetCollection<ThirdPartyJWTClaims>("ThirdPartyJWTClaims");

            var filter = !string.IsNullOrWhiteSpace(itemId) ?
                         Builders<ThirdPartyJWTClaims>.Filter.Eq(mc => mc.ItemId, itemId) :
                         Builders<ThirdPartyJWTClaims>.Filter.Empty;

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<string>> GetProjectIdsByGroupId(string projectGroupId)
        {
            var filter = Builders<Tenant>.Filter.Eq(x => x.TenantGroupId, projectGroupId);

            var tenantIds = await _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName)
                .Find(filter)
                .Project(x => x.TenantId)
                .ToListAsync();

            return tenantIds;
        }

        public async Task UpdateTenantGroupAsync(UpdateTenantGroupRequest request)
        {
            var tenantIds = await GetProjectIdsByGroupId(request.TenantGroupId);
            var collection = _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

           await collection.UpdateManyAsync(
                Builders<Tenant>.Filter.In(t => t.TenantId, tenantIds),
                Builders<Tenant>.Update.Set(t => t.Name, request.Name)
                                       .Set(t=>t.LastUpdatedBy, BlocksContext.GetContext()?.UserId)
                                       .Set(t=>t.LastUpdatedDate, DateTime.UtcNow));
        }

    }
}
