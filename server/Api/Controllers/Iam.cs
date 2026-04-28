using Blocks.Genesis;
using CloudConfiguration.DomainService.Authentication.RequestModel;
using CloudConfiguration.DomainService.IAM.RequestModel;
using CloudConfiguration.DomainService.IAM.ResponseModel;
using CloudConfiguration.DomainService.Shared.Services;
using Iam.DomainService.Accounts;
using Iam.DomainService.Activities;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.RequestModel;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class IamController : ControllerBase
    {
        private readonly IAccountService _accountService;
        private readonly IUserActivityService _userActivityService;
        private readonly IUserManagementQueryService _userManagementQueryService;
        private readonly IUserManagementMutationService _userManagementMutationService;
        private readonly IResourceMutationService _resourceMutationService;
        private readonly IResourceQueryService _resourceQueryService;
        private readonly ChangeControllerContext _changeControllerContext;
        private readonly IConfigurationService _configurationService;

        public IamController(IAccountService accountService,
                             IUserActivityService userActivityService,
                             IResourceMutationService resourceMutationService,
                             IResourceQueryService resourceQueryService,
                             IUserManagementQueryService userManagementQueryService,
                             IUserManagementMutationService userManagementMutationService,
                             ChangeControllerContext changeControllerContext, IConfigurationService configurationService)
        {
            _changeControllerContext = changeControllerContext;
            _userActivityService = userActivityService;
            _resourceMutationService = resourceMutationService;
            _resourceQueryService = resourceQueryService;
            _userManagementQueryService = userManagementQueryService;
            _userManagementMutationService = userManagementMutationService;
            _accountService = accountService;
            _configurationService = configurationService;
        }

        #region Account

        [HttpPost]
        public async Task<IActionResult> Activate([FromBody] ActivateUserRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _accountService.ActivateAccountAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        public async Task<IActionResult> Recover([FromBody] RecoveryUserRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _accountService.RecoverAccountAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _accountService.ResetAccountPasswordAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _accountService.ChangePasswordAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        public async Task<IActionResult> ResendActivation([FromBody] ResendActivationRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _accountService.ResendActivationAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        public async Task<IActionResult> ValidateActivationCode([FromBody] ValidateActivationCodeRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _accountService.ValidateAccountActivationCodeAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        #endregion

        #region Activity

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<GetSessionsResponse> GetSessions([FromQuery] BaseActivityRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _userActivityService.GetSessionsAsync(query);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<GetHistorysResponse> GetHistories([FromQuery] BaseActivityRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _userActivityService.GetHistoriesAsync(query);
        }

        #endregion

        #region Resource

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _resourceMutationService.CreatePermissionAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> UpdatePermission([FromBody] UpdatePermissionRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _resourceMutationService.UpdatePermissionAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _resourceMutationService.CreateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> UpdateRole([FromBody] UpdateRoleRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _resourceMutationService.UpdateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<GetPermissionsResponse> GetPermissions([FromBody] GetPermissionsRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _resourceQueryService.GetPermissionsAsync(query);
        }

        [HttpGet]
        [Authorize]
        public async Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverity([FromQuery] GetPermissionGroupBySeverityRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _resourceQueryService.GetPermissionsGroupBySeverityAsync();
        }

        [HttpGet()]
        [ProtectedEndPoint]
        public async Task<GetPermissionResponse> GetPermission([FromQuery] GetPermissionRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _resourceQueryService.GetPermissionAsync(query.Id);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<GetRolesResponse> GetRoles([FromBody] GetRolesRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _resourceQueryService.GetRolesAsync(query);
        }

        [HttpGet()]
        [ProtectedEndPoint]
        public async Task<GetRoleResponse> GetRole([FromQuery] GetRoleRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _resourceQueryService.GetRoleAsync(query.Id);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> SetRoles([FromBody] SetRolesRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _resourceMutationService.SetRolesAsync(command);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<List<GetResourceGroupResponse>> GetResourceGroupsAsync([FromQuery] GetResourceGroupRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _resourceQueryService.GetResourceGroupsAsync();
        }

        #endregion

        #region User

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _userManagementMutationService.CreateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> Update([FromBody] UpdateUserRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Deactivate([FromBody] DeactivateUserRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _userManagementMutationService.DeactivateUserAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> UpdateAccount([FromBody] UpdateUserRequest command)
        {
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<GetUsersResponse> GetUsers([FromBody] GetUsersRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _userManagementQueryService.GetUsersAsync(query);
        }

        [HttpGet()]
        [ProtectedEndPoint]
        public async Task<GetUserResponse> GetUser([FromQuery] GetUserRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _userManagementQueryService.GetUserAsync(query.Id);
        }

        [HttpGet()]
        [ProtectedEndPoint]
        public async Task<GetUserRolesResponse> GetUserRoles([FromQuery] GetUserRolesRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _userManagementQueryService.GetUserRolesAsync(query.Id);
        }

        [HttpGet()]
        [ProtectedEndPoint]
        public async Task<GetUserPermissionsResponse> GetUserPermissions([FromQuery] GetUserPermissionsRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            return await _userManagementQueryService.GetUserPermissionsAsync(query.Id);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<GetAccountsResponse> GetAccounts([FromBody] GetAccountsRequest query)
        {
            return await _userManagementQueryService.GetAccountsAsync(query);
        }

        [HttpGet()]
        [ProtectedEndPoint]
        public async Task<GetAccountResponse> GetAccount()
        {
            return await _userManagementQueryService.GetAccountAsync();
        }

        [HttpGet()]
        [ProtectedEndPoint]
        public async Task<GetAccountRolesResponse> GetAccountRoles()
        {
            return await _userManagementQueryService.GetAccountRolesAsync();
        }

        [HttpGet()]
        [ProtectedEndPoint]
        public async Task<GetAccountPermissionsResponse> GetAccountPermissions()
        {
            return await _userManagementQueryService.GetAccountPermissionsAsync();
        }

        [HttpPost()]
        [ProtectedEndPoint]
        public async Task<IActionResult> SaveRolesAndPermissions(SaveRolesAndPermissionsRequest command)
        {
            _changeControllerContext.ChangeContext(command);
            var result = await _userManagementMutationService.SaveRolesAndPermissionsAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet]
        public async Task<IActionResult> IsEmailAvaiable([FromQuery] IsEmailAvaiableRequest query)
        {
            _changeControllerContext.ChangeContext(query);
            var result = await _userManagementQueryService.IsUserAvailableAsync(query);
            return Ok(new IsEmailAvaiableResponse
            {
                IsAvailable = result
            });
        }

        [Authorize]
        [ProtectedEndPoint]
        [HttpGet]
        public async Task<List<UserTimeline>> GetUserTimelinesAsync(GetUserTimeLineRequest request)
        {
            return await _userManagementQueryService.GetUserTimelinesAsync(request);
        }

        #endregion

        #region Organization

        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> SaveOrganization([FromBody]  SaveOrganizationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _resourceMutationService.SaveOrganizationAsync(request);
        }

        [HttpGet]
        [Authorize]
        public async Task<GetOrganizationsResponse> GetOrganizations([FromQuery] GetOrganizationsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _resourceMutationService.GetOrganizationsAsync(request);
        }

        [HttpGet]
        [Authorize]
        public async Task<GetOrganizationResponse> GetOrganization([FromQuery]  GetOrganizationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _resourceMutationService.GetOrganizationAsync(request);
        }

        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> SaveOrganizationConfig([FromBody] SaveOrganizationConfigRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _resourceMutationService.SaveganizationConfigAsync(request);
        }

        [HttpGet]
        [Authorize]
        public async Task<OrganizationConfig> GetOrganizationConfig([FromQuery] GetOrganizationConfigRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _resourceMutationService.GetOrganizationConfigAsync(request);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<SaveSignUpSettingResponse> SaveSignUpSetting([FromBody] SaveSignUpSettingRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _accountService.SaveSingUpSettingAsync(request);
        }

        [HttpGet]
        public async Task<SignUpSetting> GetSignUpSetting([FromQuery] GetSignUpSettingRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _accountService.GetSignUpSettingAsync(request);
        }

        #endregion
        #region Cloud configuration
        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> Save([FromBody] SaveIamConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _configurationService.SaveIamConfigurationAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<GetConfigurationResponse> Get([FromQuery] GetAuthenticationConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.GetIamConfigurationAsync();
        }
        #endregion
    }
}
