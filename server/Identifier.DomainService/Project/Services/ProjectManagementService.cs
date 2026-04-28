using Blocks.Genesis;
using DomainService.Certificate;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Shared;
using DomainService.Shared.Entities;
using DomainService.Shared.Services;
using DomainService.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using StorageDriver;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;

namespace DomainService.Projects
{
    public class ProjectManagementService : IProjectManagementService
    {
        private readonly IProjectRepository _projectRepository;
        private readonly ITenants _tenants;
        private readonly ICertificateManager _certificateManager;
        private readonly IBlocksSecret _blocksSecret;
        private readonly IMessageClient _messageClient;
        private readonly IConfiguration _configuration;
        private readonly IStorageDriverService _storageDriverService;
        private readonly IEncodingService _urlEncodingService;
        private readonly ICacheClient _cacheClient;

        private const string _tenantTokenPublicCertificateCachePrefix = "tetocertpublic::";


        public ProjectManagementService(IProjectRepository projectRepository,
                                        IBlocksSecret blocksSecret,
                                        IMessageClient messageClient,
                                        IConfiguration configuration,
                                        IStorageDriverService storageDriverService,
                                        ITenants tenants,
                                        ICertificateManager certificateManager,
                                        IEncodingService urlEncodingService,
                                        ICacheClient cacheClient)
        {
            _projectRepository = projectRepository;
            _blocksSecret = blocksSecret;
            _messageClient = messageClient;
            _configuration = configuration;
            _storageDriverService = storageDriverService;
            _tenants = tenants;
            _certificateManager = certificateManager;
            _urlEncodingService = urlEncodingService;
            _cacheClient = cacheClient;
        }

        public async Task ConfigureProjectAsync(Tenant project, ProjectStatusTracer? projectStatus = null)
        {
            projectStatus ??= new ProjectStatusTracer { ProjectId = project.ItemId };

            try
            {
                (X509Certificate2 publicKeyCertificate, X509Certificate2 privateKeyCertificate) = _certificateManager.GenerateCertificates(project.JwtTokenParameters);

                await Task.WhenAll( UploadPrivateCertificateIfNeeded(projectStatus, privateKeyCertificate, project),
                                    UploadPublicCertificateIfNeeded(projectStatus, publicKeyCertificate, project));

                await Task.WhenAll(UpdateProjectIfNeeded(projectStatus, project),
                                   _projectRepository.CreateDefaultConfigurationAsync(projectStatus, project));

                await InsertIntoProjectPeopleAsync(projectStatus, project);

            }
            catch (Exception ex)
            {
                await HandleConfigurationErrorAsync(projectStatus, ex.Message);
            }
        }

        private async Task InsertIntoProjectPeopleAsync(ProjectStatusTracer statusTracer, Tenant project)
        {
            if (statusTracer.InsertedIntoProjectPeople) return;

            await _projectRepository.InsertPeopleAsync(new ProjectPeople
            {
                ItemId = Guid.NewGuid().ToString(),
                UserId = project.CreatedBy,
                TenantId = project.TenantId,
                IsCreator = true,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                IsInvitationConfirmed = true,
                IsInvitationSent = true,
                Email = BlocksContext.GetContext()?.UserName?? ""
            });

            statusTracer.InsertedIntoProjectPeople = true;

        }

        private async Task UploadPrivateCertificateIfNeeded(ProjectStatusTracer statusTracer, X509Certificate2 privateKeyCertificate, Tenant project)
        {
            if (statusTracer.IsCertificatesUploaded) return;

            await _certificateManager.UploadPrivateCertificateAsync(project.JwtTokenParameters.CertificateStorageType, privateKeyCertificate, project.JwtTokenParameters.PrivateCertificatePassword,
                _certificateManager.GeneratePrivateCertificateName(project.TenantId, project.ItemId));
        }

        private async Task UploadPublicCertificateIfNeeded(ProjectStatusTracer statusTracer, X509Certificate2 publicKeyCertificate, Tenant project)
        {
            if (statusTracer.IsCertificatesUploaded) return;

            var downloadUrl = await UploadPublicCertificateAsync(publicKeyCertificate, project);
            project.JwtTokenParameters.PublicCertificatePath = downloadUrl;
            statusTracer.IsCertificatesUploaded = true;
        }

        private async Task UpdateProjectIfNeeded(ProjectStatusTracer statusTracer, Tenant project)
        {
            if (statusTracer.IsProjectUpdated) return;

            await _projectRepository.UpdateProjectAsync(project);
            statusTracer.IsProjectUpdated = true;
        }

        private async Task HandleConfigurationErrorAsync(ProjectStatusTracer statusTracer, string errorMessage)
        {
            statusTracer.ErrorMessage = errorMessage;
            await _projectRepository.SaveStatusTracerAsync(statusTracer);
        }

        public async Task<string> UploadPublicCertificateAsync(X509Certificate2 publicKeyCertificate, Tenant project)
        {
            return project.JwtTokenParameters.CertificateStorageType switch
            {
                CertificateStorageType.Azure => await UploadPublicCertificateIntoCloudAsync(publicKeyCertificate, project),
                CertificateStorageType.Mongodb => await UploadPublicCertificateIntoCloudAsync(publicKeyCertificate, project),
                CertificateStorageType.Filefilesystem => await UploadPublicCertificateIntoFileSystemAsync(publicKeyCertificate, project),
                _ => throw new NotSupportedException($"Unsupported certificate storage type: {project.JwtTokenParameters.CertificateStorageType}"),
            };
        }

        public async Task<string> UploadPublicCertificateIntoFileSystemAsync(X509Certificate2 publicKeyCertificate, Tenant project)
        {
            FileResponse? fileResponse = new();
            string fileId = Guid.NewGuid().ToString();

            // Export the certificate to PFX bytes
            byte[] pfxBytes = publicKeyCertificate.Export(X509ContentType.Pkcs12, project.JwtTokenParameters.PublicCertificatePassword);

            // Create a memory stream from the bytes
            var memoryStream = new MemoryStream(pfxBytes);

            // Create IFormFile directly
            var formFile = new FormFile(memoryStream, 0, memoryStream.Length, "Certificate", "publicCertificate.pfx")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/x-pkcs12"
            };

            // Upload directly
            var response = await _storageDriverService.UploadFileToLocalStorageAsync(new LocalStorageUploadRequest
            {
                ItemId = fileId,
                File = formFile,
                Name = "PublicCert.pfx",
                AccessModifier = "Public"
            });

            if (response.IsSuccess)
            {
                fileResponse = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest
                {
                    FileId = fileId
                });
            }

            return fileResponse?.Url ?? "";
        }

        private async Task<string> UploadPublicCertificateIntoCloudAsync(X509Certificate2 publicKeyCertificate, Tenant project)
        {
            var fileName = $"{project.TenantId}.pfx";
            var fileId = Guid.NewGuid().ToString();
            var preSingedUri = await GetPreSingedUriForUpload(fileId, fileName);

            var content = GetByteArrayContent(publicKeyCertificate, project.JwtTokenParameters.PublicCertificatePassword);

            await UploadContentAsync(content, preSingedUri);

            var getFileResponse = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest { FileId = fileId });
            if (getFileResponse == null || string.IsNullOrWhiteSpace(getFileResponse.Url))
                throw new InvalidOperationException("Storage service did not return a valid download URL.");

            return getFileResponse.Url;
        }

        private static async Task UploadContentAsync(ByteArrayContent content, string preSignedUri)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, preSignedUri)
            {
                Content = content
            };

            request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");

            using var httpClient = new HttpClient();
            using var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        private async Task<string> GetPreSingedUriForUpload(string fileId, string fileName)
        {
            var preSignedUriRequest = new GetPreSignedUrlForUploadRequest
            {
                ItemId = fileId,
                MetaData = "{\"Title\":{\"Type\":\"String\",\"Value\":\"certificate\"}," + "\"OriginalName\":{\"Type\":\"String\",\"Value\":\"publicCertificate.pfx\"}}",
                Name = fileName,
                Tags = "[\"File\"]",
                ParentDirectoryId = string.Empty,
                AccessModifier = "Public",
                ProjectKey = BlocksContext.GetContext()?.TenantId
            };

            var presignedUrlResponse = await _storageDriverService.GetPerSignedUrlForUploadAsync(preSignedUriRequest);
            return presignedUrlResponse.UploadUrl;
        }

        private static ByteArrayContent GetByteArrayContent(X509Certificate2 certificate, string password)
        {
            byte[] bytes = certificate.Export(X509ContentType.Pkcs12, password);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-pkcs12");
            content.Headers.ContentLength = bytes.Length;
            return content;
        }

        public async Task<CreateProjectResponse> SaveProjectAsync(CreateProjectRequest project)
        {
            var groupId = string.IsNullOrEmpty(project.TenantGroupId) ? Guid.NewGuid().ToString("n") : project.TenantGroupId;
            await ManageTenantAssetAsync(project, groupId);

            foreach (var applicationContext in project.applicationContexts)
            {
                var tenant = await MapAsync(project, applicationContext, groupId);
                await Task.WhenAll(_projectRepository.SaveRepoInfoAsync(tenant, project.Resources),
                                   _projectRepository.InsertProjectAsync(tenant));

                await Task.WhenAll(_messageClient.SendToConsumerAsync(new ConsumerMessage<Tenant> { ConsumerName = IdentifierConstants.IdentifierQueueName, Payload = tenant }));
            }


            return new CreateProjectResponse { IsSuccess = true, TenantGroupId = groupId };
        }

        private async Task<string> GetDefaultDomainAsync(Resource? resource, ApplicationContext application, string tenantIdGroupId)
        {
            var tenantSlug = await _urlEncodingService.EncodeToBase26Async(tenantIdGroupId, tenantIdGroupId, 5);
            var repoSlug = await _urlEncodingService.EncodeToBase26Async(resource?.ResourceId ?? string.Empty, tenantIdGroupId, 5);
            return !string.IsNullOrEmpty(repoSlug) ? $"https://{IdentifierHelper.EnvironmentMapper(application.Environment)}{tenantSlug}-{repoSlug}{_configuration["KbtclIdentifier"]}" : $"https://{IdentifierHelper.EnvironmentMapper(application.Environment)}{tenantSlug}{_configuration["KbtclIdentifier"]}";
        }

        private async Task ManageTenantAssetAsync(CreateProjectRequest project, string customGroupId)
        {
            if (string.IsNullOrEmpty(project.TenantGroupId))
            {
                var asset = GetTenantAsset(project, customGroupId);
                await _projectRepository.UpdateTenantAssetAsync(asset);
                return;
            }

           var (assets, _) = await _projectRepository.GetTenantAssetAsync(new GetAssetRequest { TenantGroupId = project.TenantGroupId });
           project.Resources = assets.Resources ?? [];

        }

        private static TenantAsset GetTenantAsset(CreateProjectRequest createProjectRequest, string groupId)
        {
            return new TenantAsset
            {
                ItemId = Guid.NewGuid().ToString(),
                TenantGroupId = groupId,
                Resources = createProjectRequest.Resources ?? [],
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedBy = BlocksContext.GetContext()?.UserId,
            };
        }

        private async Task<Tenant> MapAsync(CreateProjectRequest createProjectRequest, ApplicationContext applicationContext, string groupId)
        {
            var certificateStorageType = GetCertificateStorageType();
            var applicationDomain = createProjectRequest.Resources?.Count > 0 ? await GetDefaultDomainAsync(createProjectRequest.Resources.First(), applicationContext, groupId) : await GetDefaultDomainAsync(createProjectRequest.Resources?.FirstOrDefault(), applicationContext, groupId);

            var project = new Tenant
            {
                ItemId = Guid.NewGuid().ToString(),
                TenantId = IdentifierHelper.EnvironmentMapper(applicationContext.Environment).ToUpper() + groupId,
                TenantGroupId = groupId,
                Environment = applicationContext.Environment,
                CreatedDate = DateTime.UtcNow,
                Name = createProjectRequest.Name,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedDate = DateTime.UtcNow,
                IsAcceptBlocksTerms = createProjectRequest.IsAcceptBlocksTerms,
                IsUseBlocksExclusively = createProjectRequest.IsUseBlocksExclusively,
                ApplicationDomain = applicationDomain,
                DbConnectionString = _blocksSecret.DatabaseConnectionString,
                CookieDomain = applicationContext.CookieDomain,
                IsDomainVerified = applicationContext.CookieDomain == IdentifierConstants.BlocsDomain,

                JwtTokenParameters = new JwtTokenParameters
                {
                    Issuer = IdentifierConstants.Issuer,
                    Subject = IdentifierConstants.Subject,
                    CertificateValidForNumberOfDays = 2 * 365,
                    IssueDate = DateTime.UtcNow,
                    Audiences = [applicationDomain],
                    PrivateCertificatePassword = Guid.NewGuid().ToString("n").Substring(7),
                    PublicCertificatePassword = Guid.NewGuid().ToString("n").Substring(7),
                    CertificateStorageType = certificateStorageType
                }
            };

            return project;
        }

        private CertificateStorageType GetCertificateStorageType()
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var certificateStorageTypeString = configuration.GetValue<string>("CertificateStorageType");

            if (!Enum.TryParse<CertificateStorageType>(certificateStorageTypeString, out var certificateStorageType))
            {
                certificateStorageType = CertificateStorageType.Azure;
            }

            return certificateStorageType;
        }

        public async Task<List<GroupedProjectsDto>> GetAllAsync(GetProjectsRequest request)
        {
            return await _projectRepository.GetAllByLastModifiedDateAsync(request);
        }

        public async Task RestoreUnfinishedProjectAsync()
        {
            var projectStatusTracers = await _projectRepository.GetAllUnfinishedProjectAsync();

            foreach (var projectStatusTracer in projectStatusTracers)
            {
                var project = await _projectRepository.GetByIdAsync(projectStatusTracer.ProjectId);
                projectStatusTracer.ErrorMessage = string.Empty;
                await ConfigureProjectAsync(project, projectStatusTracer);

                if (string.IsNullOrWhiteSpace(projectStatusTracer.ErrorMessage))
                {
                    projectStatusTracer.IsProjectCreationSuccess = true;
                    await _projectRepository.SaveStatusTracerAsync(projectStatusTracer);
                }
            }
        }

        public async Task<RestoreProjectResponse> RestoreProjectAsync(RestoreProjectRequest restoreProjectRequest)
        {
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<RestoreProjectRequest> { ConsumerName = IdentifierConstants.IdentifierQueueName, Payload = restoreProjectRequest });

            return new RestoreProjectResponse { IsSuccess = true };
        }

        public async Task<GetProjectResponse> GetAsync(string projectId)
        {
            return await MapIntoProjectAsync(projectId);
        }

        private async Task<GetProjectResponse> MapIntoProjectAsync(string projectId)
        {
            var repoProject = await _projectRepository.GetByIdAsync(projectId);

            if (repoProject == null)
            {
                return new GetProjectResponse { Errors = new Dictionary<string, string> { { "project_not_exist", $"project_with_id_{projectId}_not_exist_into_our_system" } } };
            }

            string tenantSlug = string.Empty;
            var blocksGuid = await _projectRepository.GetBlocksGuidAsync(repoProject.TenantGroupId);
            if (blocksGuid is not null)
            {
                tenantSlug = $"{IdentifierHelper.EnvironmentMapper(repoProject.Environment)}{blocksGuid.EncodedValue}";
            }

            var project = new GetProjectResponseData
            {
                Name = repoProject.Name,
                ApplicationDomain = repoProject.ApplicationDomain,
                ItemId = repoProject.ItemId,
                CreatedDate = repoProject.CreatedDate,
                LastUpdatedDate = repoProject.LastUpdatedDate,
                LastUpdatedBy = repoProject.LastUpdatedBy,
                OrganizationIds = repoProject.OrganizationIds,
                CreatedBy = repoProject.CreatedBy,
                Tags = repoProject.Tags,
                TenantId = repoProject.TenantId,
                IsDomainVerified = repoProject.IsDomainVerified,
                CookieDomain = repoProject.CookieDomain,
                IsDisabled = repoProject.IsDisabled,
                Environment = repoProject.Environment,
                TenantGroupId = repoProject.TenantGroupId,
                CustomDomain = repoProject.CustomDomain,
                TenantSlug = tenantSlug
            };

            return new GetProjectResponse { Data = project };
        }

        public async Task<BaseResponse> UpdateProjectAsync(UpdateProjectRequest request)
        {
            var project = await _projectRepository.GetByTenantIdAsync(request.ProjectKey);

            if (project == null)
            {
                return new BaseResponse() { IsSuccess = false, Errors = new Dictionary<string, string> { { "project_not_found", $"No project found with id {request.ProjectKey}" } } };
            }

            var mainDomain = IdentifierHelper.ExtractMainDomain(request.ApplicationDomain);

            if (!string.Equals(request.ApplicationDomain, project.ApplicationDomain))
            {
                project.IsDomainVerified = mainDomain == IdentifierConstants.BlocsDomain;
            }

            if (request.ApplicationDomain.Contains(IdentifierConstants.BlocsDomain, StringComparison.OrdinalIgnoreCase))
            {
                project.IsDomainVerified = true;
            }

            project.LastUpdatedDate = DateTime.UtcNow;
            project.ApplicationDomain = request.ApplicationDomain;
            project.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            project.CookieDomain = mainDomain;
            project.CustomDomain = !string.IsNullOrWhiteSpace(request.CustomDomain) ? request.CustomDomain : project.CustomDomain;
            project.JwtTokenParameters.Audiences = [request.ApplicationDomain];

            if (!string.IsNullOrWhiteSpace(request.CustomDomain) && !project.AllowedDomains.Contains(request.ApplicationDomain, StringComparer.OrdinalIgnoreCase))
            {
                project.AllowedDomains.Add(request.ApplicationDomain);
            }   

            await Task.WhenAll(_projectRepository.UpdateProjectAsync(project),
                                _projectRepository.UpdateIamConfiguration(project));

            await _tenants.UpdateTenantVersionAsync(new TenantCacheUpdateMessage
            {
                Action = "upsert",
                TenantId = project.TenantId,
                Tenant = project
            });

            //var domian = IdentifierConstants.CookieDomainPrefix + project.CookieDomain;

            //if (project.IsCookieEnable)
            //{

            //    if (applicationDomainBeforeUpdate != request.ApplicationDomain)
            //    {
            //        await _messageClient.SendToConsumerAsync(new ConsumerMessage<DisableDomainBindingRequest> { ConsumerName = IdentifierConstants.IdentifierName, Payload = new DisableDomainBindingRequest { ProjectId = project.ItemId, Domain = domian } });
            //    }

            //    await _messageClient.SendToConsumerAsync(new ConsumerMessage<ConfigureDomainRequest> { ConsumerName = IdentifierConstants.IdentifierName, Payload = new ConfigureDomainRequest { CookieDomain = domian, ProjectId = request.ProjectId } });
            //}
            //else
            //{
            //    await _messageClient.SendToConsumerAsync(new ConsumerMessage<DisableDomainBindingRequest> { ConsumerName = IdentifierConstants.IdentifierName, Payload = new DisableDomainBindingRequest { ProjectId = project.ItemId, Domain = domian } });
            //}

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<BaseResponse> DisableProjectAsync(string projectKey)
        {
            var project = _tenants.GetTenantByID(projectKey);

            if (project == null)
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "project_not_found", $"No project exist with {projectKey}" } } };
            }

            project.IsDisabled = true;
            await _projectRepository.UpdateProjectAsync(project);
            await _tenants.UpdateTenantVersionAsync(new TenantCacheUpdateMessage
            {
                Action = "upsert",
                TenantId = project.TenantId,
                Tenant = project
            });

            var domain = IdentifierConstants.CookieDomainPrefix + project.CookieDomain;
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<DisableDomainBindingRequest> { ConsumerName = IdentifierConstants.IdentifierQueueName, Payload = new DisableDomainBindingRequest { ProjectId = project.ItemId, Domain = domain } });

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<GetAssetResponse> GetAssetAsync(GetAssetRequest request)
        {
            var (assets, totalCount) = await _projectRepository.GetTenantAssetAsync(request);
            return new GetAssetResponse { Assets = assets, TotalCount = totalCount, IsSuccess = true };
        }

        public async Task<BaseResponse> AddAssetAsync(AddAssetRequest asset)
        {
            var (tenantAsset, _) = await _projectRepository.GetTenantAssetAsync(new GetAssetRequest { TenantGroupId = asset.TenantGroupId });

            tenantAsset ??= new TenantAsset
            {
                ItemId = Guid.NewGuid().ToString(),
                TenantGroupId = asset.TenantGroupId,
                Resources = [],
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedBy = BlocksContext.GetContext()?.UserId
            };

            bool isAlreadyExists = tenantAsset.Resources.Any(r => r.ResourceId == asset.Resource.ResourceId);

            if (!isAlreadyExists)
            {
                tenantAsset.Resources.Add(asset.Resource);
                tenantAsset.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
                tenantAsset.LastUpdatedDate = DateTime.UtcNow;
                await Task.WhenAll(_projectRepository.SaveTenantAssetAsync(tenantAsset),
                               _projectRepository.UpdateRepoResourceAsync(asset));
            }

            return new BaseResponse { IsSuccess = true, Errors = new Dictionary<string, string>() };
        }

        public async Task<BaseResponse> UpdateTokenValidationParametersAsync(UpdateTokenValidationParametersRequest request)
        {

            var project = await _projectRepository.GetByTenantIdAsync(request.ProjectKey);

            if (project == null)
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "project_not_found", $"No project found with id {request.ProjectKey}" } } };
            }

            project.ThirdPartyJwtTokenParameters ??= new();

            project.ThirdPartyJwtTokenParameters.ProviderName = request.ProviderName;
            project.ThirdPartyJwtTokenParameters.Audiences = request.Audiences;
            project.ThirdPartyJwtTokenParameters.Issuer = request.Issuer;
            project.ThirdPartyJwtTokenParameters.PublicCertificatePath = request.PublicCertificatePath;
            project.ThirdPartyJwtTokenParameters.PublicCertificatePassword = request.PublicCertificatePassword;
            project.ThirdPartyJwtTokenParameters.JwksUrl = request.JwksUrl;

            project.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            project.LastUpdatedDate = DateTime.UtcNow;
            await _projectRepository.UpdateProjectAsync(project);

            await Task.WhenAll( _tenants.UpdateTenantVersionAsync(new TenantCacheUpdateMessage
            {
                Action = "upsert",
                TenantId = project.TenantId,
                Tenant = project
            }), _cacheClient.RemoveKeyAsync($"{_tenantTokenPublicCertificateCachePrefix}{request.ProjectKey}"));

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<IActionResult> GetProjectTokenValidationParametersAsync(string projectId)
        {
            var project = await _projectRepository.GetByTenantIdAsync(projectId);

            if (project == null)
            {
                return new NotFoundObjectResult(new { error = "Project not found" });
            }

            var tokenParams = new
            {
                IsConfigured = project?.ThirdPartyJwtTokenParameters is not null,
                ProviderName = project?.ThirdPartyJwtTokenParameters?.ProviderName,
                Issuer = project?.ThirdPartyJwtTokenParameters?.Issuer,
                Audiences = project?.ThirdPartyJwtTokenParameters?.Audiences,
                PublicCertificatePath = project?.ThirdPartyJwtTokenParameters?.PublicCertificatePath,
                JwksUrl = project?.ThirdPartyJwtTokenParameters?.JwksUrl,
                CookieKey = project?.ThirdPartyJwtTokenParameters?.CookieKey
            };
            return new OkObjectResult(tokenParams);
        }

        public async Task<SaveThirdPartyJWTClaimsResponse> SaveThirdPartyJWTClaimsAsync(SaveThirdPartyJWTClaimsRequest request)
        {
            var claimsMapper = await MapJWTClaims(request);
            await _projectRepository.SaveJWTClaimsAsync(claimsMapper);
            return new SaveThirdPartyJWTClaimsResponse { IsSuccess = true , ItemId = claimsMapper.ItemId};
        }

        public async Task<ThirdPartyJWTClaims?> GetThirdPartyJWTClaimsAsync(GetThirdPartyJWTClaimsRequest request)
        {
            return await _projectRepository.GetThirdPartyJWTClaimsAsync(request.ItemId);
        }

        private async Task<ThirdPartyJWTClaims> MapJWTClaims(SaveThirdPartyJWTClaimsRequest request)
        {
            var thirdPartyClaims = !string.IsNullOrWhiteSpace(request.ItemId)?
                                    await _projectRepository.GetThirdPartyJWTClaimsAsync(request.ItemId):
                                    new ThirdPartyJWTClaims { ItemId = Guid.NewGuid().ToString(), CreatedBy = BlocksContext.GetContext()?.UserId , CreatedDate = DateTime.UtcNow};


            thirdPartyClaims.UserId = request.UserId;
            thirdPartyClaims.UserName = request.UserName;
            thirdPartyClaims.Email = request.Email;
            thirdPartyClaims.Roles = request.Roles;
            thirdPartyClaims.Name = request.Name;
            thirdPartyClaims.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            thirdPartyClaims.LastUpdatedDate = DateTime.UtcNow;

            return thirdPartyClaims;

        }

        public async Task<BaseResponse> UpdateTenantGroupAsync(UpdateTenantGroupRequest request)
        {
            await _projectRepository.UpdateTenantGroupAsync(request);

            return new BaseResponse { IsSuccess = true };
        }

    }

}
