using FluentAssertions;
using FluentValidation.TestHelper;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Moq;

namespace XUnitTest.Resources.Validators
{
    public class CreatePermissionValidatorTests
    {
        private readonly Mock<IResourceRepository> _resourceRepositoryMock;
        private readonly Mock<IIdentityAccessManagementService> _iamServiceMock;
        private readonly CreatePermissionValidator _validator;

        public CreatePermissionValidatorTests()
        {
            _resourceRepositoryMock = new Mock<IResourceRepository>();
            _iamServiceMock = new Mock<IIdentityAccessManagementService>();
            _validator = new CreatePermissionValidator(_resourceRepositoryMock.Object, _iamServiceMock.Object);
        }

        #region Base Validation Tests

        [Fact]
        public async Task Validate_WithCompletelyValidRequest_PassesAllValidations()
        {
            // Arrange
            var request = CreateValidRequest();
            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync((Permission)null);
            _iamServiceMock.Setup(x => x.IsRoot()).Returns(false);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        #endregion

        #region Resource Uniqueness Validation Tests

        [Fact]
        public async Task Validate_WithNonExistingResource_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Resource = "api/new/unique";

            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync((Permission)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Resource);
        }

        [Fact]
        public async Task Validate_WithExistingResource_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Resource = "api/existing/resource";

            var existingPermission = new Permission 
            { 
                ItemId = "existing-id", 
                Resource = request.Resource 
            };

            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync(existingPermission);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Resource)
                .WithErrorMessage("Resource_Already_Exists");
        }

        [Theory]
        [InlineData("Service::Controller::Action")]
        [InlineData("api/users/read")]
        [InlineData("frontend/action/view")]
        public async Task Validate_WithDifferentResourceFormats_ChecksUniqueness(string resource)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Resource = resource;

            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(resource))
                .ReturnsAsync((Permission)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Resource);
            _resourceRepositoryMock.Verify(x => x.GetPermissionByResourceAsync(resource), Times.Once);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task Validate_WithMultipleErrors_IncludesResourceExistsError()
        {
            // Arrange
            var request = new CreatePermissionRequest
            {
                Name = null,
                Type = ResourceType.None,
                Resource = "existing-resource",
                ResourceGroup = "",
                IsBuiltIn = true
            };

            var existingPermission = new Permission { ItemId = "existing-id" };
            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync(existingPermission);
            _iamServiceMock.Setup(x => x.IsRoot()).Returns(false);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Resource_Already_Exists");
        }

        #endregion

        #region Helper Methods

        private CreatePermissionRequest CreateValidRequest()
        {
            return new CreatePermissionRequest
            {
                Name = "Test Permission",
                Type = ResourceType.FrontendAction,
                Resource = "test-resource",
                ResourceGroup = "test-group",
                Description = "Test Description",
                IsBuiltIn = false,
                Tags = new List<string> { "tag1" },
                DependentPermissions = new List<string>()
            };
        }

        #endregion
    }

    public class UpdatePermissionValidatorTests
    {
        private readonly Mock<IResourceRepository> _resourceRepositoryMock;
        private readonly Mock<IIdentityAccessManagementService> _iamServiceMock;
        private readonly UpdatePermissionValidator _validator;

        public UpdatePermissionValidatorTests()
        {
            _resourceRepositoryMock = new Mock<IResourceRepository>();
            _iamServiceMock = new Mock<IIdentityAccessManagementService>();
            _validator = new UpdatePermissionValidator(_resourceRepositoryMock.Object, _iamServiceMock.Object);
        }

        #region Base Validation Tests

        [Fact]
        public async Task Validate_WithCompletelyValidRequest_PassesAllValidations()
        {
            // Arrange
            var request = CreateValidRequest();
            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync((Permission)null);
            _iamServiceMock.Setup(x => x.IsRoot()).Returns(false);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        #endregion

        #region Resource Uniqueness Validation Tests

        [Fact]
        public async Task Validate_WithNonExistingResource_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Resource = "api/new/unique";

            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync((Permission)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Resource);
        }

        [Fact]
        public async Task Validate_WithSameResourceButDifferentId_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.ItemId = "update-id";
            request.Resource = "api/existing/resource";

            var existingPermission = new Permission 
            { 
                ItemId = "different-id", 
                Resource = request.Resource 
            };

            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync(existingPermission);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Resource)
                .WithErrorMessage("Resource_Already_Exists");
        }

        [Fact]
        public async Task Validate_WithSameResourceAndSameId_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.ItemId = "same-id";
            request.Resource = "api/my/resource";

            var existingPermission = new Permission 
            { 
                ItemId = "same-id", 
                Resource = request.Resource 
            };

            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync(existingPermission);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Resource);
        }

        [Theory]
        [InlineData("update-id-1", "other-id-1", false)]
        [InlineData("update-id-2", "update-id-2", true)]
        [InlineData("update-id-3", null, true)]
        public async Task Validate_WithVariousIdCombinations_ValidatesCorrectly(
            string updateId, string existingId, bool shouldPass)
        {
            // Arrange
            var request = CreateValidRequest();
            request.ItemId = updateId;
            request.Resource = "api/test/resource";

            var existingPermission = existingId != null 
                ? new Permission { ItemId = existingId, Resource = request.Resource }
                : null;

            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync(existingPermission);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            if (shouldPass)
            {
                result.ShouldNotHaveValidationErrorFor(x => x.Resource);
            }
            else
            {
                result.ShouldHaveValidationErrorFor(x => x.Resource)
                    .WithErrorMessage("Resource_Already_Exists");
            }
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task Validate_WithMultipleErrors_IncludesResourceExistsError()
        {
            // Arrange
            var request = new UpdatePermissionRequest
            {
                ItemId = "update-id",
                Name = null,
                Type = ResourceType.None,
                Resource = "existing-resource",
                ResourceGroup = "",
                IsBuiltIn = true,
                IsArchived = false
            };

            var existingPermission = new Permission 
            { 
                ItemId = "different-id", 
                Resource = request.Resource 
            };

            _resourceRepositoryMock.Setup(x => x.GetPermissionByResourceAsync(request.Resource))
                .ReturnsAsync(existingPermission);
            _iamServiceMock.Setup(x => x.IsRoot()).Returns(false);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Resource_Already_Exists");
        }

        #endregion

        #region Helper Methods

        private UpdatePermissionRequest CreateValidRequest()
        {
            return new UpdatePermissionRequest
            {
                ItemId = "update-id",
                Name = "Test Permission",
                Type = ResourceType.FrontendAction,
                Resource = "test-resource",
                ResourceGroup = "test-group",
                Description = "Test Description",
                IsBuiltIn = false,
                IsArchived = false,
                Tags = new List<string> { "tag1" },
                DependentPermissions = new List<string>()
            };
        }

        #endregion
    }

    public class RoleValidatorTests
    {
        private readonly Mock<IResourceRepository> _resourceRepositoryMock;
        private readonly RoleValidator _validator;

        public RoleValidatorTests()
        {
            _resourceRepositoryMock = new Mock<IResourceRepository>();
            _validator = new RoleValidator(_resourceRepositoryMock.Object);
        }

        #region Name Validation Tests

        [Fact]
        public async Task Validate_WithValidName_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Name = "Valid Role Name";

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Validate_WithEmptyOrNullName_FailsValidation(string name)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Name = name;

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Name);
        }

        [Fact]
        public async Task Validate_WithNameExceeding150Characters_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Name = new string('A', 151);

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Name)
                .WithErrorMessage("Maximum_Character_Limit_150");
        }

        [Fact]
        public async Task Validate_WithNameExactly150Characters_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Name = new string('A', 150);

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Name);
        }

        #endregion

        #region Slug Validation Tests

        [Theory]
        [InlineData("admin")]
        [InlineData("user-role")]
        [InlineData("valid_slug")]
        [InlineData("slug-with-dashes")]
        public async Task Validate_WithValidSlug_PassesValidation(string slug)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = slug;

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(slug))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Slug);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Validate_WithEmptyOrNullSlug_FailsValidation(string slug)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = slug;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Slug);
        }

        [Theory]
        [InlineData("slug with space")]
        [InlineData("invalid slug")]
        [InlineData("test role")]
        public async Task Validate_WithSlugContainingSpaces_FailsValidation(string slug)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = slug;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Slug)
                .WithErrorMessage("Resource name must not contain spaces");
        }

        [Fact]
        public async Task Validate_WithSlugExceeding200Characters_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = new string('a', 201);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Slug)
                .WithErrorMessage("Resource name maximum character limit 200");
        }

        [Fact]
        public async Task Validate_WithSlugExactly200Characters_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = new string('a', 200);

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Slug);
        }

        #endregion

        #region Slug Uniqueness Validation Tests

        [Fact]
        public async Task Validate_WithNonExistingSlug_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = "unique-slug";

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Slug);
            _resourceRepositoryMock.Verify(x => x.GetRoleBySlugAsync(request.Slug), Times.Once);
        }

        [Fact]
        public async Task Validate_WithExistingSlug_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = "existing-slug";

            var existingRole = new Role 
            { 
                ItemId = "existing-id", 
                Slug = request.Slug 
            };

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug))
                .ReturnsAsync(existingRole);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Slug)
                .WithErrorMessage("Role slug must be unique");
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("user")]
        [InlineData("guest")]
        public async Task Validate_WithDifferentSlugs_ChecksUniqueness(string slug)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = slug;

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(slug))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Slug);
            _resourceRepositoryMock.Verify(x => x.GetRoleBySlugAsync(slug), Times.Once);
        }

        #endregion

        #region Cascade Mode Tests

        [Fact]
        public async Task Validate_WithNullSlug_StopsAtFirstError()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = null;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Slug);
            // Repository should not be called because validation stops at NotEmpty
            _resourceRepositoryMock.Verify(x => x.GetRoleBySlugAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Validate_WithSlugWithSpaces_StopsBeforeUniquenessCheck()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = "slug with spaces";

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Slug);
            // Repository should not be called because validation stops at spaces check
            _resourceRepositoryMock.Verify(x => x.GetRoleBySlugAsync(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task Validate_WithCompletelyValidRequest_PassesAllValidations()
        {
            // Arrange
            var request = CreateValidRequest();

            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug))
                .ReturnsAsync((Role)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task Validate_WithMultipleErrors_ReportsAllErrors()
        {
            // Arrange
            var request = new CreateRoleRequest
            {
                Name = null,
                Slug = null,
                Description = "Test"
            };

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.ShouldHaveValidationErrorFor(x => x.Name);
            result.ShouldHaveValidationErrorFor(x => x.Slug);
        }

        [Fact]
        public async Task Validate_WithValidDataButExistingSlug_OnlyFailsOnSlug()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Slug = "existing";

            var existingRole = new Role { ItemId = "id", Slug = "existing" };
            _resourceRepositoryMock.Setup(x => x.GetRoleBySlugAsync(request.Slug))
                .ReturnsAsync(existingRole);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.ShouldNotHaveValidationErrorFor(x => x.Name);
            result.ShouldHaveValidationErrorFor(x => x.Slug)
                .WithErrorMessage("Role slug must be unique");
        }

        #endregion

        #region Helper Methods

        private CreateRoleRequest CreateValidRequest()
        {
            return new CreateRoleRequest
            {
                Name = "Test Role",
                Slug = "test-role",
                Description = "Test Role Description"
            };
        }

        #endregion
    }
}
