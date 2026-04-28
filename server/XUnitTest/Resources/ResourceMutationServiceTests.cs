using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Resources
{
    public class ResourceMutationServiceTests : IDisposable
    {
        private readonly Mock<ILogger<ResourceMutationService>> _loggerMock;
        private readonly Mock<IResourceRepository> _resourceRepositoryMock;
        private readonly Mock<IIdentityAccessManagementService> _iamServiceMock;
        private readonly Mock<IValidator<CreatePermissionRequest>> _permissionValidatorMock;
        private readonly Mock<IValidator<UpdatePermissionRequest>> _updatePermissionValidatorMock;
        private readonly Mock<IValidator<CreateRoleRequest>> _roleValidatorMock;
        private readonly ResourceMutationService _resourceMutationService;

        public ResourceMutationServiceTests()
        {
            _loggerMock = new Mock<ILogger<ResourceMutationService>>();
            _resourceRepositoryMock = new Mock<IResourceRepository>();
            _iamServiceMock = new Mock<IIdentityAccessManagementService>();
            _permissionValidatorMock = new Mock<IValidator<CreatePermissionRequest>>();
            _updatePermissionValidatorMock = new Mock<IValidator<UpdatePermissionRequest>>();
            _roleValidatorMock = new Mock<IValidator<CreateRoleRequest>>();

            _resourceMutationService = new ResourceMutationService(
                _loggerMock.Object,
                _resourceRepositoryMock.Object,
                _iamServiceMock.Object,
                _permissionValidatorMock.Object,
                _updatePermissionValidatorMock.Object,
                _roleValidatorMock.Object
            );
        }

        public void Dispose()
        {
            BlocksContext.ClearContext();
        }

        #region CreatePermissionAsync Tests

        [Fact]
        public async Task CreatePermissionAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = CreateValidPermissionRequest();
            SetupBlocksContext("user-123");
            SetupValidValidation(_permissionValidatorMock, request);
            _resourceRepositoryMock.Setup(x => x.InsertPermissionAsync(It.IsAny<Permission>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, It.IsAny<ResourceMutationEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.CreatePermissionAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrEmpty();
            _resourceRepositoryMock.Verify(x => x.InsertPermissionAsync(It.IsAny<Permission>()), Times.Once);
            _iamServiceMock.Verify(x => x.SendToQueueAsync(Constants.IamQueue, It.IsAny<ResourceMutationEvent>()), Times.Once);
        }

        [Fact]
        public async Task CreatePermissionAsync_WithValidationError_ReturnsErrors()
        {
            // Arrange
            var request = CreateValidPermissionRequest();
            var validationFailure = new ValidationFailure("Name", "Name is required");
            SetupInvalidValidation(_permissionValidatorMock, request, validationFailure);

            // Act
            var result = await _resourceMutationService.CreatePermissionAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Name");
            result.Errors["Name"].Should().Be("Name is required");
            _resourceRepositoryMock.Verify(x => x.InsertPermissionAsync(It.IsAny<Permission>()), Times.Never);
        }

        [Fact]
        public async Task CreatePermissionAsync_ConvertsResourceToLowerCase()
        {
            // Arrange
            var request = CreateValidPermissionRequest();
            request.Resource = "API/USERS/READ";
            Permission capturedPermission = null;

            SetupBlocksContext("user-123");
            SetupValidValidation(_permissionValidatorMock, request);
            _resourceRepositoryMock.Setup(x => x.InsertPermissionAsync(It.IsAny<Permission>()))
                .Callback<Permission>(p => capturedPermission = p)
                .ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, It.IsAny<ResourceMutationEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            await _resourceMutationService.CreatePermissionAsync(request);

            // Assert
            capturedPermission.Should().NotBeNull();
            capturedPermission.Resource.Should().Be("api/users/read");
        }

        #endregion

        #region CreateRoleAsync Tests

        [Fact]
        public async Task CreateRoleAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = CreateValidRoleRequest();
            SetupBlocksContext("user-456");
            SetupValidValidation(_roleValidatorMock, request);
            _resourceRepositoryMock.Setup(x => x.InsertRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, It.IsAny<ResourceMutationEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.CreateRoleAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrEmpty();
            _resourceRepositoryMock.Verify(x => x.InsertRoleAsync(It.IsAny<Role>()), Times.Once);
        }

        [Fact]
        public async Task CreateRoleAsync_WithValidationError_ReturnsErrors()
        {
            // Arrange
            var request = CreateValidRoleRequest();
            var validationFailure = new ValidationFailure("Slug", "Slug is required");
            SetupInvalidValidation(_roleValidatorMock, request, validationFailure);

            // Act
            var result = await _resourceMutationService.CreateRoleAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Slug");
            _resourceRepositoryMock.Verify(x => x.InsertRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task CreateRoleAsync_ConvertsSlugToLowerCase()
        {
            // Arrange
            var request = CreateValidRoleRequest();
            request.Slug = "ADMIN-ROLE";
            Role capturedRole = null;

            SetupBlocksContext("user-789");
            SetupValidValidation(_roleValidatorMock, request);
            _resourceRepositoryMock.Setup(x => x.InsertRoleAsync(It.IsAny<Role>()))
                .Callback<Role>(r => capturedRole = r)
                .ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, It.IsAny<ResourceMutationEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            await _resourceMutationService.CreateRoleAsync(request);

            // Assert
            capturedRole.Should().NotBeNull();
            capturedRole.Slug.Should().Be("admin-role");
        }

        #endregion

        #region UpdatePermissionAsync Tests

        [Fact]
        public async Task UpdatePermissionAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = CreateValidUpdatePermissionRequest();
            var existingPermission = new Permission { ItemId = request.ItemId, Resource = "old-resource" };

            SetupBlocksContext("user-update");
            _resourceRepositoryMock.Setup(x => x.GetPermissionByIdAsync(request.ItemId)).ReturnsAsync(existingPermission);
            SetupValidValidation(_updatePermissionValidatorMock, request);
            _resourceRepositoryMock.Setup(x => x.UpdatePermissionAsync(It.IsAny<Permission>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, It.IsAny<ResourceMutationEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.UpdatePermissionAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be(request.ItemId);
            _resourceRepositoryMock.Verify(x => x.UpdatePermissionAsync(It.IsAny<Permission>()), Times.Once);
        }

        [Fact]
        public async Task UpdatePermissionAsync_WithNonExistentPermission_ReturnsItemNotFoundError()
        {
            // Arrange
            var request = CreateValidUpdatePermissionRequest();
            _resourceRepositoryMock.Setup(x => x.GetPermissionByIdAsync(request.ItemId)).ReturnsAsync((Permission)null);

            // Act
            var result = await _resourceMutationService.UpdatePermissionAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ItemId");
            result.Errors["ItemId"].Should().Be("Item_Not_Found");
            _resourceRepositoryMock.Verify(x => x.UpdatePermissionAsync(It.IsAny<Permission>()), Times.Never);
        }

        [Fact]
        public async Task UpdatePermissionAsync_WithValidationError_ReturnsErrors()
        {
            // Arrange
            var request = CreateValidUpdatePermissionRequest();
            var existingPermission = new Permission { ItemId = request.ItemId };
            var validationFailure = new ValidationFailure("Name", "Name is required");

            _resourceRepositoryMock.Setup(x => x.GetPermissionByIdAsync(request.ItemId)).ReturnsAsync(existingPermission);
            SetupInvalidValidation(_updatePermissionValidatorMock, request, validationFailure);

            // Act
            var result = await _resourceMutationService.UpdatePermissionAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Name");
            _resourceRepositoryMock.Verify(x => x.UpdatePermissionAsync(It.IsAny<Permission>()), Times.Never);
        }

        [Fact]
        public async Task UpdatePermissionAsync_WhenUpdateFails_ReturnsFailure()
        {
            // Arrange
            var request = CreateValidUpdatePermissionRequest();
            var existingPermission = new Permission { ItemId = request.ItemId };

            SetupBlocksContext("user-fail");
            _resourceRepositoryMock.Setup(x => x.GetPermissionByIdAsync(request.ItemId)).ReturnsAsync(existingPermission);
            SetupValidValidation(_updatePermissionValidatorMock, request);
            _resourceRepositoryMock.Setup(x => x.UpdatePermissionAsync(It.IsAny<Permission>())).ReturnsAsync(false);

            // Act
            var result = await _resourceMutationService.UpdatePermissionAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
        }

        #endregion

        #region UpdateRoleAsync Tests

        [Fact]
        public async Task UpdateRoleAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = CreateValidUpdateRoleRequest();
            var existingRole = new Role { ItemId = request.ItemId, Slug = "admin" };

            SetupBlocksContext("user-role-update");
            _resourceRepositoryMock.Setup(x => x.GetRoleByIdAsync(request.ItemId)).ReturnsAsync(existingRole);
            _resourceRepositoryMock.Setup(x => x.UpdateRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, It.IsAny<ResourceMutationEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.UpdateRoleAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be(request.ItemId);
            _resourceRepositoryMock.Verify(x => x.UpdateRoleAsync(It.IsAny<Role>()), Times.Once);
        }

        [Theory]
        [InlineData(null, "Should_Not_Be_Empty_Null")]
        [InlineData("", "Should_Not_Be_Empty_Null")]
        [InlineData("   ", "Should_Not_Be_Empty_Null")]
        public async Task UpdateRoleAsync_WithInvalidName_ReturnsValidationError(string name, string expectedError)
        {
            // Arrange
            var request = CreateValidUpdateRoleRequest();
            request.Name = name;
            var existingRole = new Role { ItemId = request.ItemId };

            _resourceRepositoryMock.Setup(x => x.GetRoleByIdAsync(request.ItemId)).ReturnsAsync(existingRole);

            // Act
            var result = await _resourceMutationService.UpdateRoleAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Name");
            result.Errors["Name"].Should().Be(expectedError);
        }

        [Fact]
        public async Task UpdateRoleAsync_WithNameExceeding150Characters_ReturnsValidationError()
        {
            // Arrange
            var request = CreateValidUpdateRoleRequest();
            request.Name = new string('A', 151);
            var existingRole = new Role { ItemId = request.ItemId };

            _resourceRepositoryMock.Setup(x => x.GetRoleByIdAsync(request.ItemId)).ReturnsAsync(existingRole);

            // Act
            var result = await _resourceMutationService.UpdateRoleAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Name");
            result.Errors["Name"].Should().Be("Maximum_Character_Limit_100");
        }

        [Fact]
        public async Task UpdateRoleAsync_WithNonExistentRole_ReturnsItemNotFoundError()
        {
            // Arrange
            var request = CreateValidUpdateRoleRequest();
            _resourceRepositoryMock.Setup(x => x.GetRoleByIdAsync(request.ItemId)).ReturnsAsync((Role)null);

            // Act
            var result = await _resourceMutationService.UpdateRoleAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ItemId");
            result.Errors["ItemId"].Should().Be("Item not found");
        }

        [Fact]
        public async Task UpdateRoleAsync_WhenUpdateFails_ReturnsFailure()
        {
            // Arrange
            var request = CreateValidUpdateRoleRequest();
            var existingRole = new Role { ItemId = request.ItemId };

            SetupBlocksContext("user-fail");
            _resourceRepositoryMock.Setup(x => x.GetRoleByIdAsync(request.ItemId)).ReturnsAsync(existingRole);
            _resourceRepositoryMock.Setup(x => x.UpdateRoleAsync(It.IsAny<Role>())).ReturnsAsync(false);

            // Act
            var result = await _resourceMutationService.UpdateRoleAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
        }

        #endregion

        #region SetRolesAsync Tests

        [Fact]
        public async Task SetRolesAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = new SetRolesRequest
            {
                Slug = "admin",
                AddPermissions = new List<string> { "perm1", "perm2" },
                RemovePermissions = new List<string> { "perm3" }
            };
            var existingRole = new Role { Slug = "admin" };

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug)).ReturnsAsync(existingRole);
            _resourceRepositoryMock.Setup(x => x.UpdateRolePermissionByIdsAsync(request.Slug, request.AddPermissions))
                .ReturnsAsync(true);
            _resourceRepositoryMock.Setup(x => x.RemoveRolePermissionByIdsAsync(request.Slug, request.RemovePermissions))
                .ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, It.IsAny<ResourceSetToPermissionMutationEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.SetRolesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            _resourceRepositoryMock.Verify(x => x.UpdateRolePermissionByIdsAsync(request.Slug, request.AddPermissions), Times.Once);
            _resourceRepositoryMock.Verify(x => x.RemoveRolePermissionByIdsAsync(request.Slug, request.RemovePermissions), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SetRolesAsync_WithEmptySlug_ReturnsFailure(string slug)
        {
            // Arrange
            var request = new SetRolesRequest { Slug = slug };

            // Act
            var result = await _resourceMutationService.SetRolesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            _resourceRepositoryMock.Verify(x => x.GetRoleBySlugAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SetRolesAsync_WithNonExistentRole_ReturnsFailure()
        {
            // Arrange
            var request = new SetRolesRequest { Slug = "non-existent" };
            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug)).ReturnsAsync((Role)null);

            // Act
            var result = await _resourceMutationService.SetRolesAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
        }

        [Fact]
        public async Task SetRolesAsync_WithOnlyAddPermissions_UpdatesOnlyAdd()
        {
            // Arrange
            var request = new SetRolesRequest
            {
                Slug = "admin",
                AddPermissions = new List<string> { "perm1" },
                RemovePermissions = new List<string>()
            };
            var existingRole = new Role { Slug = "admin" };

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug)).ReturnsAsync(existingRole);
            _resourceRepositoryMock.Setup(x => x.UpdateRolePermissionByIdsAsync(request.Slug, request.AddPermissions))
                .ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, It.IsAny<ResourceSetToPermissionMutationEvent>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.SetRolesAsync(request);

            // Assert
            result.Success.Should().BeTrue();
            _resourceRepositoryMock.Verify(x => x.UpdateRolePermissionByIdsAsync(request.Slug, request.AddPermissions), Times.Once);
            _resourceRepositoryMock.Verify(x => x.RemoveRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never);
        }

        #endregion

        #region ExecuteResourceMutationCommandAsync Tests

        [Fact]
        public async Task ExecuteResourceMutationCommandAsync_WithNullCommand_LogsWarningAndReturns()
        {
            // Act
            await _resourceMutationService.ExecuteResourceMutationCommandAsync(null);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("null ResourceMutationEvent")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteResourceMutationCommandAsync_WithPermissionEntity_ProcessesPermission()
        {
            // Arrange
            var command = new ResourceMutationEvent
            {
                Entity = ResourceEntity.Permission,
                ItemId = "perm-123",
                Action = MutationEventType.Create
            };
            var permission = new Permission { ItemId = command.ItemId };

            SetupBlocksContext("user-exec");
            _resourceRepositoryMock.Setup(x => x.GetPermissionByIdAsync(command.ItemId)).ReturnsAsync(permission);
            _resourceRepositoryMock.Setup(x => x.SaveResourceTimelineAsync(It.IsAny<ResourceTimeline<Permission>>()))
                .ReturnsAsync(true);

            // Act
            await _resourceMutationService.ExecuteResourceMutationCommandAsync(command);

            // Assert
            _resourceRepositoryMock.Verify(x => x.GetPermissionByIdAsync(command.ItemId), Times.Once);
            _resourceRepositoryMock.Verify(x => x.SaveResourceTimelineAsync(It.IsAny<ResourceTimeline<Permission>>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteResourceMutationCommandAsync_WithRoleEntity_ProcessesRole()
        {
            // Arrange
            var command = new ResourceMutationEvent
            {
                Entity = ResourceEntity.Role,
                ItemId = "role-123",
                Action = MutationEventType.Update
            };
            var role = new Role { ItemId = command.ItemId, Slug = "admin" };

            SetupBlocksContext("user-exec-role");
            _resourceRepositoryMock.Setup(x => x.GetRoleByIdAsync(command.ItemId)).ReturnsAsync(role);
            _resourceRepositoryMock.Setup(x => x.SaveResourceTimelineAsync(It.IsAny<ResourceTimeline<Role>>()))
                .ReturnsAsync(true);
            _resourceRepositoryMock.Setup(x => x.UpdateRolesCountAsync(role.Slug)).ReturnsAsync(true);

            // Act
            await _resourceMutationService.ExecuteResourceMutationCommandAsync(command);

            // Assert
            _resourceRepositoryMock.Verify(x => x.GetRoleByIdAsync(command.ItemId), Times.Once);
            _resourceRepositoryMock.Verify(x => x.SaveResourceTimelineAsync(It.IsAny<ResourceTimeline<Role>>()), Times.Once);
            _resourceRepositoryMock.Verify(x => x.UpdateRolesCountAsync(role.Slug), Times.Once);
        }

        #endregion

        #region ProcessPermissionAsync Tests

        [Fact]
        public async Task ProcessPermissionAsync_WithAddAndRemovePermissions_ProcessesAll()
        {
            // Arrange
            var command = new ResourceSetToPermissionMutationEvent
            {
                Entity = ResourceEntity.Role,
                Slug = "admin",
                AddPermissions = new List<string> { "perm1", "perm2" },
                RemovePermissions = new List<string> { "perm3" }
            };

            SetupBlocksContext("user-process");
            _resourceRepositoryMock.Setup(x => x.GetPermissionByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new Permission { ItemId = "test" });
            _resourceRepositoryMock.Setup(x => x.SaveResourceTimelinesAsync(It.IsAny<List<ResourceTimeline<Permission>>>()))
                .ReturnsAsync(true);
            _resourceRepositoryMock.Setup(x => x.UpdateRolesCountAsync(command.Slug)).ReturnsAsync(true);

            // Act
            var result = await _resourceMutationService.ProcessPermissionAsync(command);

            // Assert
            result.Should().BeTrue();
            _resourceRepositoryMock.Verify(x => x.GetPermissionByIdAsync(It.IsAny<string>()), Times.Exactly(3));
            _resourceRepositoryMock.Verify(x => x.SaveResourceTimelinesAsync(It.Is<List<ResourceTimeline<Permission>>>(l => l.Count == 3)), Times.Once);
            _resourceRepositoryMock.Verify(x => x.UpdateRolesCountAsync(command.Slug), Times.Once);
        }

        #endregion

        #region Organization Tests

        [Fact]
        public async Task SaveOrganizationAsync_WithNewOrganization_CreatesNew()
        {
            // Arrange
            var request = new SaveOrganizationRequest
            {
                ItemId = Guid.NewGuid().ToString(),
                Name = "New Org",
                IsEnable = true
            };

            SetupBlocksContext("user-org");
            _resourceRepositoryMock.Setup(x => x.GetOrganizationById(request.ItemId)).ReturnsAsync((Organization)null);
            _resourceRepositoryMock.Setup(x => x.SaveOrganizationAsync(It.IsAny<Organization>())).Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.SaveOrganizationAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            _resourceRepositoryMock.Verify(x => x.SaveOrganizationAsync(It.Is<Organization>(o => 
                o.Name == request.Name && o.IsEnable == request.IsEnable)), Times.Once);
        }

        [Fact]
        public async Task SaveOrganizationAsync_WithExistingOrganization_UpdatesExisting()
        {
            // Arrange
            var existingOrg = new Organization { ItemId = "org-existing", Name = "Old Name" };
            var request = new SaveOrganizationRequest
            {
                ItemId = existingOrg.ItemId,
                Name = "Updated Name",
                IsEnable = false
            };

            SetupBlocksContext("user-org-update");
            _resourceRepositoryMock.Setup(x => x.GetOrganizationById(request.ItemId)).ReturnsAsync(existingOrg);
            _resourceRepositoryMock.Setup(x => x.SaveOrganizationAsync(It.IsAny<Organization>())).Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.SaveOrganizationAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _resourceRepositoryMock.Verify(x => x.SaveOrganizationAsync(It.Is<Organization>(o => 
                o.ItemId == existingOrg.ItemId && o.Name == request.Name)), Times.Once);
        }

        [Fact]
        public async Task GetOrganizationsAsync_ReturnsOrganizations()
        {
            // Arrange
            var request = new GetOrganizationsRequest { Page = 0, PageSize = 10 };
            var response = new GetOrganizationsResponse 
            { 
                IsSuccess = true, 
                TotalCount = 5,
                Organizations = new List<Organization>()
            };

            _resourceRepositoryMock.Setup(x => x.GetOrganizationsAsync(request)).ReturnsAsync(response);

            // Act
            var result = await _resourceMutationService.GetOrganizationsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.TotalCount.Should().Be(5);
        }

        [Fact]
        public async Task GetOrganizationAsync_WithValidId_ReturnsOrganization()
        {
            // Arrange
            var request = new GetOrganizationRequest { ItemId = "org-123" };
            var organization = new Organization { ItemId = request.ItemId, Name = "Test Org" };

            _resourceRepositoryMock.Setup(x => x.GetOrganizationById(request.ItemId)).ReturnsAsync(organization);

            // Act
            var result = await _resourceMutationService.GetOrganizationAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.Organization.Should().Be(organization);
        }

        #endregion

        #region OrganizationConfig Tests

        [Fact]
        public async Task SaveganizationConfigAsync_WithNewConfig_CreatesNew()
        {
            // Arrange
            var request = new SaveOrganizationConfigRequest
            {
                ItemId = Guid.NewGuid().ToString(),
                AllowCreationFromCloud = true,
                AllowCreationFromConstruct = false,
                IsMultiOrgEnabled = true,
                Roles = new List<string> { "admin", "user" }
            };

            SetupBlocksContext("user-config");
            _resourceRepositoryMock.Setup(x => x.GetOrgConfigByIdAsync(request.ItemId)).ReturnsAsync((OrganizationConfig)null);
            _resourceRepositoryMock.Setup(x => x.SaveOrganizationConfig(It.IsAny<OrganizationConfig>())).Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.SaveganizationConfigAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            _resourceRepositoryMock.Verify(x => x.SaveOrganizationConfig(It.Is<OrganizationConfig>(c => 
                c.AllowCreationFromCloud == request.AllowCreationFromCloud &&
                c.IsMultiOrgEnabled == request.IsMultiOrgEnabled)), Times.Once);
        }

        [Fact]
        public async Task SaveganizationConfigAsync_WithExistingConfig_UpdatesExisting()
        {
            // Arrange
            var existingConfig = new OrganizationConfig { ItemId = "config-existing" };
            var request = new SaveOrganizationConfigRequest
            {
                ItemId = existingConfig.ItemId,
                AllowCreationFromCloud = false,
                AllowCreationFromConstruct = true,
                IsMultiOrgEnabled = false
            };

            SetupBlocksContext("user-config-update");
            _resourceRepositoryMock.Setup(x => x.GetOrgConfigByIdAsync(request.ItemId)).ReturnsAsync(existingConfig);
            _resourceRepositoryMock.Setup(x => x.SaveOrganizationConfig(It.IsAny<OrganizationConfig>())).Returns(Task.CompletedTask);

            // Act
            var result = await _resourceMutationService.SaveganizationConfigAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _resourceRepositoryMock.Verify(x => x.SaveOrganizationConfig(It.Is<OrganizationConfig>(c => 
                c.ItemId == existingConfig.ItemId)), Times.Once);
        }

        [Fact]
        public async Task GetOrganizationConfigAsync_ReturnsConfig()
        {
            // Arrange
            var request = new GetOrganizationConfigRequest();
            var config = new OrganizationConfig { ItemId = "config-123", IsMultiOrgEnabled = true };

            _resourceRepositoryMock.Setup(x => x.GetOrganizationConfigAsync()).ReturnsAsync(config);

            // Act
            var result = await _resourceMutationService.GetOrganizationConfigAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.ItemId.Should().Be("config-123");
            result.IsMultiOrgEnabled.Should().BeTrue();
        }

        #endregion

        #region Event Sending Tests

        [Fact]
        public async Task SendResourceMutationEventAsync_SendsEventToQueue()
        {
            // Arrange
            var resourceMutation = new ResourceMutationEvent
            {
                Action = MutationEventType.Create,
                ItemId = "test-123",
                Entity = ResourceEntity.Permission
            };

            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, resourceMutation))
                .Returns(Task.CompletedTask);

            // Act
            await _resourceMutationService.SendResourceMutationEventAsync(resourceMutation);

            // Assert
            _iamServiceMock.Verify(x => x.SendToQueueAsync(Constants.IamQueue, resourceMutation), Times.Once);
        }

        [Fact]
        public async Task SendResourceSetToPermissionMutationEventAsync_SendsEventToQueue()
        {
            // Arrange
            var resourceMutation = new ResourceSetToPermissionMutationEvent
            {
                Entity = ResourceEntity.Role,
                Slug = "admin",
                AddPermissions = new List<string> { "perm1" }
            };

            _iamServiceMock.Setup(x => x.SendToQueueAsync(Constants.IamQueue, resourceMutation))
                .Returns(Task.CompletedTask);

            // Act
            await _resourceMutationService.SendResourceSetToPermissionMutationEventAsync(resourceMutation);

            // Assert
            _iamServiceMock.Verify(x => x.SendToQueueAsync(Constants.IamQueue, resourceMutation), Times.Once);
        }

        #endregion

        #region Helper Methods

        private CreatePermissionRequest CreateValidPermissionRequest()
        {
            return new CreatePermissionRequest
            {
                Name = "Test Permission",
                Description = "Test Description",
                Resource = "api/test",
                Type = ResourceType.Endpoint,
                Tags = new List<string> { "tag1" },
                IsBuiltIn = false,
                ResourceGroup = "TestGroup",
                DependentPermissions = new List<string>()
            };
        }

        private CreateRoleRequest CreateValidRoleRequest()
        {
            return new CreateRoleRequest
            {
                Name = "Test Role",
                Description = "Test Role Description",
                Slug = "test-role"
            };
        }

        private UpdatePermissionRequest CreateValidUpdatePermissionRequest()
        {
            return new UpdatePermissionRequest
            {
                ItemId = "perm-update-123",
                Name = "Updated Permission",
                Description = "Updated Description",
                Resource = "api/updated",
                Type = ResourceType.Endpoint,
                Tags = new List<string> { "tag2" },
                IsArchived = false,
                IsBuiltIn = false,
                ResourceGroup = "UpdatedGroup",
                DependentPermissions = new List<string>()
            };
        }

        private UpdateRoleRequest CreateValidUpdateRoleRequest()
        {
            return new UpdateRoleRequest
            {
                ItemId = "role-update-123",
                Name = "Updated Role",
                Description = "Updated Role Description"
            };
        }

        private void SetupBlocksContext(string userId)
        {
            var createMethods = typeof(BlocksContext).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .ToList();

            var create15Method = createMethods.FirstOrDefault(m => m.GetParameters().Length == 15);

            if (create15Method != null)
            {
                var context = (BlocksContext)create15Method.Invoke(null, new object[]
                {
                    "test-tenant", Array.Empty<string>(), userId, true, string.Empty, string.Empty,
                    DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                    "testuser", string.Empty, "Test User", string.Empty, "test-tenant", string.Empty
                });
                BlocksContext.SetContext(context, true);
            }
            else
            {
                var create14Method = createMethods.FirstOrDefault(m => m.GetParameters().Length == 14);

                if (create14Method != null)
                {
                    var context = (BlocksContext)create14Method.Invoke(null, new object[]
                    {
                        "test-tenant", Array.Empty<string>(), userId, true, string.Empty, string.Empty,
                        DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                        "testuser", string.Empty, "Test User", string.Empty, "test-tenant"
                    });
                    BlocksContext.SetContext(context, true);
                }
            }
        }

        private void SetupValidValidation<T>(Mock<IValidator<T>> validatorMock, T request)
        {
            validatorMock.Setup(x => x.ValidateAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
        }

        private void SetupInvalidValidation<T>(Mock<IValidator<T>> validatorMock, T request, ValidationFailure failure)
        {
            var validationResult = new ValidationResult(new[] { failure });
            validatorMock.Setup(x => x.ValidateAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(validationResult);
        }

        #endregion
    }
}
