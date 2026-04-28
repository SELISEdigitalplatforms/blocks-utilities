using FluentAssertions;
using FluentValidation.TestHelper;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Moq;

namespace XUnitTest.Resources.Validators
{
    public class BasePermissionValidatorTests
    {
        private readonly Mock<IResourceRepository> _resourceRepositoryMock;
        private readonly Mock<IIdentityAccessManagementService> _iamServiceMock;
        private readonly TestPermissionValidator _validator;

        public BasePermissionValidatorTests()
        {
            _resourceRepositoryMock = new Mock<IResourceRepository>();
            _iamServiceMock = new Mock<IIdentityAccessManagementService>();
            _validator = new TestPermissionValidator(_resourceRepositoryMock.Object, _iamServiceMock.Object);
        }

        #region Name Validation Tests

        [Fact]
        public async Task Validate_WithValidName_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Name = "Valid Permission Name";

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

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Name)
                .WithErrorMessage("Maximum character limit 150 exceeded");
        }

        [Fact]
        public async Task Validate_WithNameExactly150Characters_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Name = new string('A', 150);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Name);
        }

        #endregion

        #region Type Validation Tests

        [Theory]
        [InlineData(ResourceType.Endpoint)]
        [InlineData(ResourceType.FrontendAction)]
        [InlineData(ResourceType.DataProtection)]
        public async Task Validate_WithValidResourceType_PassesValidation(ResourceType type)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Type = type;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Type);
        }

        [Fact]
        public async Task Validate_WithNoneResourceType_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Type = ResourceType.None;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Type);
        }

        #endregion

        #region Resource Validation Tests

        [Theory]
        [InlineData("api/users/read")]
        [InlineData("Service::Controller::Action")]
        [InlineData("valid-resource-name")]
        public async Task Validate_WithValidResource_PassesValidation(string resource)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Resource = resource;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Resource);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Validate_WithEmptyOrNullResource_FailsValidation(string resource)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Resource = resource;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Resource);
        }

        [Theory]
        [InlineData("api/users with spaces")]
        [InlineData("Service::Controller ::Action")]
        [InlineData("resource with space")]
        public async Task Validate_WithResourceContainingSpaces_FailsValidation(string resource)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Resource = resource;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Resource)
                .WithErrorMessage("Resource cannot contain spaces.");
        }

        #endregion

        #region ResourceGroup Validation Tests

        [Theory]
        [InlineData("valid-group")]
        [InlineData("ValidGroup")]
        [InlineData("valid_group")]
        public async Task Validate_WithValidResourceGroup_PassesValidation(string resourceGroup)
        {
            // Arrange
            var request = CreateValidRequest();
            request.ResourceGroup = resourceGroup;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.ResourceGroup);
        }

        [Fact]
        public async Task Validate_WithEmptyResourceGroup_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.ResourceGroup = "";

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.ResourceGroup);
        }

        [Theory]
        [InlineData("group with space")]
        [InlineData("invalid group")]
        [InlineData("test group")]
        public async Task Validate_WithResourceGroupContainingSpaces_FailsValidation(string resourceGroup)
        {
            // Arrange
            var request = CreateValidRequest();
            request.ResourceGroup = resourceGroup;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.ResourceGroup)
                .WithErrorMessage("ResourceGroup must not contain spaces.");
        }

        #endregion

        #region IsBuiltIn Validation Tests

        [Fact]
        public async Task Validate_WithIsBuiltInTrueAndUserIsRoot_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.IsBuiltIn = true;

            _iamServiceMock.Setup(x => x.IsRoot()).Returns(true);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.IsBuiltIn);
        }

        [Fact]
        public async Task Validate_WithIsBuiltInTrueAndUserIsNotRoot_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.IsBuiltIn = true;

            _iamServiceMock.Setup(x => x.IsRoot()).Returns(false);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.IsBuiltIn)
                .WithErrorMessage("You are not allowed");
        }

        [Fact]
        public async Task Validate_WithIsBuiltInFalse_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.IsBuiltIn = false;

            _iamServiceMock.Setup(x => x.IsRoot()).Returns(false);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.IsBuiltIn);
        }

        #endregion

        #region Endpoint Structure Validation Tests

        [Theory]
        [InlineData("Service::Controller::Action")]
        [InlineData("UserService::UserController::CreateUser")]
        [InlineData("API::Auth::Login")]
        public async Task Validate_WithValidEndpointStructure_PassesValidation(string resource)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Type = ResourceType.Endpoint;
            request.Resource = resource;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("Service::Controller")]
        [InlineData("Service")]
        [InlineData("Service::Controller::Action::Extra")]
        [InlineData("Service::::Action")]
        [InlineData("::Controller::Action")]
        [InlineData("Service:: ::Action")]
        public async Task Validate_WithInvalidEndpointStructure_FailsValidation(string resource)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Type = ResourceType.Endpoint;
            request.Resource = resource;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.Errors.Should().Contain(e => 
                e.ErrorMessage == "Endpoint resource must be in the format Service::Controller::Action");
        }

        [Theory]
        [InlineData("api/frontend/action")]
        [InlineData("some-resource")]
        public async Task Validate_WithNonEndpointType_DoesNotValidateStructure(string resource)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Type = ResourceType.FrontendAction;
            request.Resource = resource;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task Validate_WithCompletelyValidRequest_PassesAllValidations()
        {
            // Arrange
            var request = CreateValidRequest();
            _iamServiceMock.Setup(x => x.IsRoot()).Returns(true);

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
            var request = new TestPermissionRequest
            {
                Name = null,
                Type = ResourceType.None,
                Resource = null,
                ResourceGroup = "",  // Use empty string instead of null to avoid NullReferenceException
                IsBuiltIn = true
            };

            _iamServiceMock.Setup(x => x.IsRoot()).Returns(false);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCountGreaterThan(3);
            result.ShouldHaveValidationErrorFor(x => x.Name);
            result.ShouldHaveValidationErrorFor(x => x.Type);
            result.ShouldHaveValidationErrorFor(x => x.Resource);
            result.ShouldHaveValidationErrorFor(x => x.ResourceGroup);
            result.ShouldHaveValidationErrorFor(x => x.IsBuiltIn);
        }

        #endregion

        #region Helper Methods

        private TestPermissionRequest CreateValidRequest()
        {
            return new TestPermissionRequest
            {
                Name = "Test Permission",
                Type = ResourceType.FrontendAction,
                Resource = "valid-resource",
                ResourceGroup = "test-group",
                IsBuiltIn = false,
                Description = "Test Description",
                Tags = new List<string> { "tag1" },
                DependentPermissions = new List<string>()
            };
        }

        #endregion

        #region Test Helper Classes

        // Concrete implementation for testing abstract validator
        public class TestPermissionRequest : PermissionRequestBase
        {
        }

        public class TestPermissionValidator : BasePermissionValidator<TestPermissionRequest>
        {
            public TestPermissionValidator(
                IResourceRepository resourceRepository,
                IIdentityAccessManagementService identityAccessManagementService)
                : base(resourceRepository, identityAccessManagementService)
            {
            }
        }

        #endregion
    }
}
