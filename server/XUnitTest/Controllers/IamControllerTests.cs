using Api.Controllers;
using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Activities;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Controllers
{
    public class IamControllerTests
    {
        private readonly Mock<IAccountService> _accountService = new();
        private readonly Mock<IUserActivityService> _activityService = new();
        private readonly Mock<IUserManagementQueryService> _userQueryService = new();
        private readonly Mock<IUserManagementMutationService> _userMutationService = new();
        private readonly Mock<IResourceMutationService> _resourceMutationService = new();
        private readonly Mock<IResourceQueryService> _resourceQueryService = new();
        private readonly Mock<ChangeControllerContext> _changeContext = new(new Mock<ITenants>().Object, new Mock<IDbContextProvider>().Object, new Mock<IHttpContextAccessor>().Object);
        private readonly IamController _controller;
        private readonly Mock<IConfigurationService> _cloudConfig = new();

        public IamControllerTests()
        {
            _controller = new IamController(_accountService.Object, _activityService.Object, _resourceMutationService.Object, _resourceQueryService.Object, _userQueryService.Object, _userMutationService.Object, _changeContext.Object, _cloudConfig.Object);
        }

        private IamController CreateController()
        {
            var controller = new IamController(
                _accountService.Object,
                _activityService.Object,
                _resourceMutationService.Object,
                _resourceQueryService.Object,
                _userQueryService.Object,
                _userMutationService.Object,
                _changeContext.Object,
                _cloudConfig.Object
            );

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        [Fact]
        public async Task Activate_WhenSuccess_ReturnsOk()
        {
            // Arrange
            var command = new ActivateUserRequest
            {
                Code = "code"
            };

            var serviceResponse = new BaseAccountResponse
            {
                IsSuccess = true
            };

            _accountService
                .Setup(x => x.ActivateAccountAsync(command))
                .ReturnsAsync(serviceResponse);

            var controller = CreateController();

            // Act
            var result = await controller.Activate(command);

            // Assert
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().Be(serviceResponse);
        }

        [Fact]
        public async Task Activate_WhenFailure_ReturnsBadRequest()
        {
            // Arrange
            var command = new ActivateUserRequest
            {
                Code = "invalid"
            };

            var serviceResponse = new BaseAccountResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>{ { "Code", "Invalid" }}};

            _accountService
                .Setup(x => x.ActivateAccountAsync(command))
                .ReturnsAsync(serviceResponse);

            var controller = CreateController();

            // Act
            var result = await controller.Activate(command);

            // Assert
            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            badRequest.Value.Should().Be(serviceResponse);
        }

        [Fact]
        public async Task Recover_WhenValid_ReturnsOk()
        {
            var command = new RecoveryUserRequest
            {
                Email = "test@test.com"
            };

            var response = new BaseAccountResponse
            {
                IsSuccess = true
            };

            _accountService
                .Setup(x => x.RecoverAccountAsync(command))
                .ReturnsAsync(response);

            var controller = CreateController();

            var result = await controller.Recover(command);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task ResetPassword_ReturnsOkResult()
        {
            var request = new ResetPasswordRequest { Code = "reset-code", Password = "NewPass123!" };
            var response = new BaseAccountResponse { IsSuccess = true };
            _accountService.Setup(x => x.ResetAccountPasswordAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.ResetPassword(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task ChangePassword_ReturnsOkResult()
        {
            var request = new ChangePasswordRequest { OldPassword = "Old123!", NewPassword = "New123!" };
            var response = new BaseAccountResponse { IsSuccess = true };
            _accountService.Setup(x => x.ChangePasswordAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.ChangePassword(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task ResendActivation_ReturnsOkResult()
        {
            var request = new ResendActivationRequest { UserId = "test@test.com" };
            var response = new BaseAccountResponse { IsSuccess = true };
            _accountService.Setup(x => x.ResendActivationAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.ResendActivation(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task ValidateActivationCode_ReturnsOkResult()
        {
            var request = new ValidateActivationCodeRequest { ActivationCode = "validation-code" };
            var response = new ActivationCodeValidationResponse { IsSuccess = true };
            _accountService.Setup(x => x.ValidateAccountActivationCodeAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.ValidateActivationCode(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetSessions_ReturnsGetSessionsResponse()
        {
            var request = new BaseActivityRequest();
            var response = new GetSessionsResponse();
            _activityService.Setup(x => x.GetSessionsAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetSessions(request);

            result.Should().BeOfType<GetSessionsResponse>();
        }

        [Fact]
        public async Task GetHistories_ReturnsGetHistorysResponse()
        {
            var request = new BaseActivityRequest();
            var response = new GetHistorysResponse();
            _activityService.Setup(x => x.GetHistoriesAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetHistories(request);

            result.Should().BeOfType<GetHistorysResponse>();
        }

        [Fact]
        public async Task CreatePermission_ReturnsOkResult()
        {
            var request = new CreatePermissionRequest { Name = "TestPermission" };
            var response = new BaseMutationResponse { IsSuccess = true };
            _resourceMutationService.Setup(x => x.CreatePermissionAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.CreatePermission(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task UpdatePermission_ReturnsOkResult()
        {
            var request = new UpdatePermissionRequest { ItemId = "test-item-1", Name = "UpdatedPermission" };
            var response = new BaseMutationResponse { IsSuccess = true };
            _resourceMutationService.Setup(x => x.UpdatePermissionAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.UpdatePermission(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task CreateRole_ReturnsOkResult()
        {
            var request = new CreateRoleRequest { Name = "TestRole" };
            var response = new BaseMutationResponse { IsSuccess = true };
            _resourceMutationService.Setup(x => x.CreateRoleAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.CreateRole(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task UpdateRole_ReturnsOkResult()
        {
            var request = new UpdateRoleRequest { ItemId = "test-item-1", Name = "UpdatedRole" };
            var response = new BaseMutationResponse { IsSuccess = true };
            _resourceMutationService.Setup(x => x.UpdateRoleAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.UpdateRole(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetPermissions_ReturnsGetPermissionsResponse()
        {
            var request = new GetPermissionsRequest();
            var response = new GetPermissionsResponse();
            _resourceQueryService.Setup(x => x.GetPermissionsAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetPermissions(request);

            result.Should().BeOfType<GetPermissionsResponse>();
        }

        [Fact]
        public async Task GetPermission_ReturnsGetPermissionResponse()
        {
            var request = new GetPermissionRequest { Id = "test-item-1" };
            var response = new GetPermissionResponse();
            _resourceQueryService.Setup(x => x.GetPermissionAsync(request.Id)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetPermission(request);

            result.Should().BeOfType<GetPermissionResponse>();
        }

        [Fact]
        public async Task GetRoles_ReturnsGetRolesResponse()
        {
            var request = new GetRolesRequest();
            var response = new GetRolesResponse();
            _resourceQueryService.Setup(x => x.GetRolesAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetRoles(request);

            result.Should().BeOfType<GetRolesResponse>();
        }

        [Fact]
        public async Task GetRole_ReturnsGetRoleResponse()
        {
            var request = new GetRoleRequest { Id = "test-item-1" };
            var response = new GetRoleResponse();
            _resourceQueryService.Setup(x => x.GetRoleAsync(request.Id)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetRole(request);

            result.Should().BeOfType<GetRoleResponse>();
        }

        [Fact]
        public async Task GetResourceGroupsAsync_ReturnsListOfGetResourceGroupResponse()
        {
            var request = new GetResourceGroupRequest();
            var response = new List<GetResourceGroupResponse>();
            _resourceQueryService.Setup(x => x.GetResourceGroupsAsync()).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetResourceGroupsAsync(request);

            result.Should().BeOfType<List<GetResourceGroupResponse>>();
        }


        [Fact]
        public async Task Create_ReturnsOkResult()
        {
            var request = new CreateUserRequest { Email = "newuser@test.com" };
            var response = new BaseMutationResponse { IsSuccess = true };
            _userMutationService.Setup(x => x.CreateUserAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.Create(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsOkResult()
        {
            var request = new UpdateUserRequest { ItemId = "test-id-1" };
            var response = new BaseMutationResponse { IsSuccess = true };
            _userMutationService.Setup(x => x.UpdateUserAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.Update(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Deactivate_ReturnsOkResult()
        {
            var request = new DeactivateUserRequest { UserId = "test-user-id" };
            var response = new BaseResponse { IsSuccess = true };
            _userMutationService.Setup(x => x.DeactivateUserAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.Deactivate(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task UpdateAccount_ReturnsOkResult()
        {
            var request = new UpdateUserRequest { ItemId = "test-id-1" };
            var response = new BaseMutationResponse { IsSuccess = true };
            _userMutationService.Setup(x => x.UpdateUserAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.UpdateAccount(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetUsers_ReturnsGetUsersResponse()
        {
            var request = new GetUsersRequest();
            var response = new GetUsersResponse();
            _userQueryService.Setup(x => x.GetUsersAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetUsers(request);

            result.Should().BeOfType<GetUsersResponse>();
        }

        [Fact]
        public async Task GetUser_ReturnsGetUserResponse()
        {
            var request = new GetUserRequest { Id = "test-id" };
            var response = new GetUserResponse();
            _userQueryService.Setup(x => x.GetUserAsync(request.Id)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetUser(request);

            result.Should().BeOfType<GetUserResponse>();
        }

        [Fact]
        public async Task GetUserRoles_ReturnsGetUserRolesResponse()
        {
            var request = new GetUserRolesRequest { Id = "test-id" };
            var response = new GetUserRolesResponse();
            _userQueryService.Setup(x => x.GetUserRolesAsync(request.Id)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetUserRoles(request);

            result.Should().BeOfType<GetUserRolesResponse>();
        }

        [Fact]
        public async Task GetUserPermissions_ReturnsGetUserPermissionsResponse()
        {
            var request = new GetUserPermissionsRequest { Id = "test-id" };
            var response = new GetUserPermissionsResponse();
            _userQueryService.Setup(x => x.GetUserPermissionsAsync(request.Id)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetUserPermissions(request);

            result.Should().BeOfType<GetUserPermissionsResponse>();
        }

        [Fact]
        public async Task GetAccounts_ReturnsGetAccountsResponse()
        {
            var request = new GetAccountsRequest();
            var response = new GetAccountsResponse();
            _userQueryService.Setup(x => x.GetAccountsAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetAccounts(request);

            result.Should().BeOfType<GetAccountsResponse>();
        }

        [Fact]
        public async Task GetAccount_ReturnsGetAccountResponse()
        {
            var response = new GetAccountResponse();
            _userQueryService.Setup(x => x.GetAccountAsync()).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetAccount();

            result.Should().BeOfType<GetAccountResponse>();
        }

        [Fact]
        public async Task GetAccountRoles_ReturnsGetAccountRolesResponse()
        {
            var response = new GetAccountRolesResponse();
            _userQueryService.Setup(x => x.GetAccountRolesAsync()).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetAccountRoles();

            result.Should().BeOfType<GetAccountRolesResponse>();
        }

        [Fact]
        public async Task GetAccountPermissions_ReturnsGetAccountPermissionsResponse()
        {
            var response = new GetAccountPermissionsResponse();
            _userQueryService.Setup(x => x.GetAccountPermissionsAsync()).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetAccountPermissions();

            result.Should().BeOfType<GetAccountPermissionsResponse>();
        }

        [Fact]
        public async Task SaveRolesAndPermissions_ReturnsOkResult()
        {
            var request = new SaveRolesAndPermissionsRequest { UserId = "test-user-id" };
            var response = new BaseMutationResponse { IsSuccess = true };
            _userMutationService.Setup(x => x.SaveRolesAndPermissionsAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.SaveRolesAndPermissions(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task IsEmailAvaiable_ReturnsOkResult()
        {
            var request = new IsEmailAvaiableRequest { Email = "test@test.com" };
            _userQueryService.Setup(x => x.IsUserAvailableAsync(request)).ReturnsAsync(true);
            var controller = CreateController();

            var result = await controller.IsEmailAvaiable(request);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetUserTimelinesAsync_ReturnsListOfUserTimeline()
        {
            var request = new GetUserTimeLineRequest();
            var response = new List<UserTimeline>();
            _userQueryService.Setup(x => x.GetUserTimelinesAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetUserTimelinesAsync(request);

            result.Should().BeOfType<List<UserTimeline>>();
        }

        [Fact]
        public async Task SaveOrganization_ReturnsBaseResponse()
        {
            var request = new SaveOrganizationRequest { Name = "Test Org" };
            var response = new BaseResponse { IsSuccess = true };
            _resourceMutationService.Setup(x => x.SaveOrganizationAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.SaveOrganization(request);

            result.Should().BeOfType<BaseResponse>();
        }

        [Fact]
        public async Task GetOrganizations_ReturnsGetOrganizationsResponse()
        {
            var request = new GetOrganizationsRequest();
            var response = new GetOrganizationsResponse();
            _resourceMutationService.Setup(x => x.GetOrganizationsAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetOrganizations(request);

            result.Should().BeOfType<GetOrganizationsResponse>();
        }

        [Fact]
        public async Task GetOrganization_ReturnsGetOrganizationResponse()
        {
            var request = new GetOrganizationRequest { ItemId = "test-id" };
            var response = new GetOrganizationResponse();
            _resourceMutationService.Setup(x => x.GetOrganizationAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.GetOrganization(request);

            result.Should().BeOfType<GetOrganizationResponse>();
        }

        [Fact]
        public async Task SaveOrganizationConfig_ReturnsBaseResponse()
        {
            var request = new SaveOrganizationConfigRequest { ItemId = "test-id" };
            var response = new BaseResponse { IsSuccess = true };
            _resourceMutationService.Setup(x => x.SaveganizationConfigAsync(request)).ReturnsAsync(response);
            var controller = CreateController();

            var result = await controller.SaveOrganizationConfig(request);

            result.Should().BeOfType<BaseResponse>();
        }
    }
}
