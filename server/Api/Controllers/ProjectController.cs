using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class ProjectController : ControllerBase
    {
        private readonly IProjectManagementService _projectManagementService;
        private readonly IValidator<CreateProjectRequest> _createProjectValidator;
        private readonly IValidator<UpdateProjectRequest> _updateProjectValidator;
        private readonly ChangeControllerContext _changeControllerContext;

        public ProjectController(IProjectManagementService projectManagementService,
                                 IValidator<CreateProjectRequest> createProjectValidator,
                                 IValidator<UpdateProjectRequest> updateProjectValidator,
                                 ChangeControllerContext changeControllerContext)
        {
            _projectManagementService = projectManagementService;
            _createProjectValidator = createProjectValidator;
            _updateProjectValidator = updateProjectValidator;
            _changeControllerContext = changeControllerContext;
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<CreateProjectResponse> Create([FromBody] CreateProjectRequest request)
        {
            var validationResult = await _createProjectValidator.ValidateAsync(request);

            if (!validationResult.IsValid)
            {
                return new CreateProjectResponse { Errors = validationResult.Errors.ToDictionary(e => string.IsNullOrWhiteSpace(e.PropertyName) ? "validation_error" : e.PropertyName, e => e.ErrorMessage), IsSuccess = false };
            }

            return await _projectManagementService.SaveProjectAsync(request);
        }


        [HttpGet]
        [ProtectedEndPoint]
        public async Task<List<GroupedProjectsDto>> Gets([FromQuery] GetProjectsRequest request)
        {
            return await _projectManagementService.GetAllAsync(request);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<RestoreProjectResponse> Restore([FromBody] RestoreProjectRequest restoreProjectRequest)
        {
            return await _projectManagementService.RestoreProjectAsync(restoreProjectRequest);
        }

        [ProtectedEndPoint]
        [HttpGet]
        public async Task<GetProjectResponse> Get([FromQuery] string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return new GetProjectResponse { Errors = new Dictionary<string, string> { { "empty_project_id", "projectId_should_not_be_empty" } } };

            return await _projectManagementService.GetAsync(projectId);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseResponse> UpdateProject([FromBody] UpdateProjectRequest request)
        {
            var validationResult = await _updateProjectValidator.ValidateAsync(request);

            if (!validationResult.IsValid)
            {
                return new BaseResponse { IsSuccess = false, Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            return await _projectManagementService.UpdateProjectAsync(request);
        }

        [Authorize]
        [HttpPost]
        public async Task<BaseResponse> UpdateTenantGroup([FromBody] UpdateTenantGroupRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TenantGroupId))
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "property_missing", "TenantGroupId or ProjectNane should not be empty" } } };
            }

             return await _projectManagementService.UpdateTenantGroupAsync(request);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseResponse> Disable([FromBody] DisableProjectRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectKey))
            {
                return new AuthConfigResponse { Errors = new Dictionary<string, string> { { "missing_projectKey", "ProjectKey is required" } } };
            }

            return await _projectManagementService.DisableProjectAsync(request.ProjectKey);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<GetAssetResponse> GetAsset([FromQuery] GetAssetRequest request)
        {
            return await _projectManagementService.GetAssetAsync(request);   
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<BaseResponse> AddAsset([FromBody] AddAssetRequest asset)
        {
            if (string.IsNullOrWhiteSpace(asset.TenantGroupId) || asset.Resource == null)
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "invalid_asset", "Asset or GroupId cannot be null or empty" } } };
            }

            await _projectManagementService.AddAssetAsync(asset);
            return new BaseResponse { IsSuccess = true };
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<BaseResponse> UpdateTokenValidationParameters([FromBody] UpdateTokenValidationParametersRequest request)
        {
            return await _projectManagementService.UpdateTokenValidationParametersAsync(request);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<IActionResult> GetTokenValidationParameters([FromQuery] GetTokenValidationParametersRequest request)
        {
            return await _projectManagementService.GetProjectTokenValidationParametersAsync(request.ProjectKey);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<SaveThirdPartyJWTClaimsResponse> SaveThirdPartyJWTClaims([FromBody] SaveThirdPartyJWTClaimsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _projectManagementService.SaveThirdPartyJWTClaimsAsync(request);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<ThirdPartyJWTClaims?> GetThirdPartyJWTClaims([FromQuery] GetThirdPartyJWTClaimsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _projectManagementService.GetThirdPartyJWTClaimsAsync(request);
        }
    }
}
