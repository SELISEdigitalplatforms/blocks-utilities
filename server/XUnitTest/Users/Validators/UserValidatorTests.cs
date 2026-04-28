using Blocks.Genesis;
using FluentAssertions;
using FluentValidation.TestHelper;
using Iam.DomainService.Configurations;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Moq;
using Tenant = Blocks.Genesis.Tenant;

namespace XUnitTest.Users.Validators
{
    public class CreateUserValidatorTests : IDisposable
    {
        private readonly Mock<IUserRepository> _userRepositoryMock;
        private readonly Mock<IIamConfigurationRepository> _configRepositoryMock;
        private readonly CreateUserValidator _validator;

        public CreateUserValidatorTests()
        {
            _userRepositoryMock = new Mock<IUserRepository>();
            _configRepositoryMock = new Mock<IIamConfigurationRepository>();
            
            SetupBlocksContext();
            
            _validator = new CreateUserValidator(_userRepositoryMock.Object, _configRepositoryMock.Object);
        }

        public void Dispose()
        {
            BlocksContext.ClearContext();
        }

        private static void SetupBlocksContext(string userId = "user-123", string tenantId = "tenant-123")
        {
            var createMethods = typeof(BlocksContext).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .ToList();

            var create15Method = createMethods.FirstOrDefault(m => m.GetParameters().Length == 15);

            if (create15Method != null)
            {
                var context = (BlocksContext)create15Method.Invoke(null, new object[]
                {
                    tenantId, Array.Empty<string>(), userId, true, string.Empty, string.Empty,
                    DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                    "testuser", string.Empty, "Test User", string.Empty, tenantId, string.Empty
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
                        tenantId, Array.Empty<string>(), userId, true, string.Empty, string.Empty,
                        DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                        "testuser", string.Empty, "Test User", string.Empty, tenantId
                    });
                    BlocksContext.SetContext(context, true);
                }
            }
        }

        private static CreateUserRequest CreateValidRequest()
        {
            return new CreateUserRequest
            {
                Email = "newuser@example.com",
                UserName = "testuser123",
                Password = "Test@Pass123",
                FirstName = "John",
                LastName = "Doe",
                UserPassType = UserPassType.Password,
                UserCreationType = UserCreationType.Portal,
                UserMfaType = UserMfaType.TOTP,
                MfaEnabled = false
            };
        }

        #region Complete Validation Tests

        [Fact]
        public async Task Validate_WithCompletelyValidRequest_PassesAllValidations()
        {
            // Arrange
            var request = CreateValidRequest();
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User)null);
            _userRepositoryMock.Setup(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _configRepositoryMock.Setup(x => x.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration 
                { 
                    PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$" 
                });

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        #endregion

        #region FirstName Validation Tests

        [Fact]
        public async Task Validate_FirstName_WithNull_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.FirstName = null;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.FirstName);
        }

        [Fact]
        public async Task Validate_FirstName_WithEmptyString_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.FirstName = "";
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.FirstName);
        }

        [Theory]
        [InlineData(150)]
        [InlineData(100)]
        [InlineData(50)]
        public async Task Validate_FirstName_WithinLimit_PassesValidation(int length)
        {
            // Arrange
            var request = CreateValidRequest();
            request.FirstName = new string('A', length);
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.FirstName);
        }

        [Fact]
        public async Task Validate_FirstName_ExceedsMaxLength_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.FirstName = new string('A', 151);
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.FirstName)
                .WithErrorMessage("Maximum character limit 150 exceeded");
        }

        #endregion

        #region LastName Validation Tests

        [Fact]
        public async Task Validate_LastName_ExceedsMaxLength_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.LastName = new string('B', 151);
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.LastName)
                .WithErrorMessage("Maximum character limit 150 exceeded");
        }

        #endregion

        #region UserName Validation Tests

        [Theory]
        [InlineData(3)]
        [InlineData(2)]
        [InlineData(1)]
        public async Task Validate_UserName_BelowMinLength_FailsValidation(int length)
        {
            // Arrange
            var request = CreateValidRequest();
            request.UserName = new string('u', length);
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.UserName)
                .WithErrorMessage("User name must be within 4 to 40 characters in length");
        }

        [Theory]
        [InlineData(4)]
        [InlineData(20)]
        [InlineData(100)]
        public async Task Validate_UserName_WithinRange_PassesValidation(int length)
        {
            // Arrange
            var request = CreateValidRequest();
            request.UserName = new string('u', length);
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User)null);
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.UserName);
        }

        [Fact]
        public async Task Validate_UserName_ExceedsMaxLength_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.UserName = new string('u', 101);
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.UserName);
        }

        [Fact]
        public async Task Validate_UserName_AlreadyExists_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.UserName = "existinguser";

            var existingUser = new User { ItemId = "existing-id", UserName = "existinguser" };
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync("existinguser", It.IsAny<string>()))
                .ReturnsAsync(existingUser);
            _userRepositoryMock.Setup(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _configRepositoryMock.Setup(x => x.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration 
                { 
                    PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$" 
                });

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.UserName)
                .WithErrorMessage("User name already exists");
        }

        [Fact]
        public async Task Validate_UserName_Null_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.UserName = null;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.UserName);
        }

        #endregion

        #region Password Validation Tests

        [Fact]
        public async Task Validate_Password_Weak_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Password = "weak";
            
            _configRepositoryMock.Setup(x => x.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration 
                { 
                    PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$" 
                });
            _userRepositoryMock.Setup(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Password)
                .WithErrorMessage("Password weak. Ensure at least one lower and upper case letter, one special character, one digit and minimum 8 characters length");
        }

        [Fact]
        public async Task Validate_Password_Blacklisted_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Password = "BlackListed@123";
            
            _configRepositoryMock.Setup(x => x.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration 
                { 
                    PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$" 
                });
            _userRepositoryMock.Setup(x => x.CheckPasswordBlackListedAsync("BlackListed@123", It.IsAny<string>()))
                .ReturnsAsync(true);
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Password)
                .WithErrorMessage("This password can not be used.");
        }

        [Fact]
        public async Task Validate_Password_Null_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Password = null;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Password);
        }

        [Fact]
        public async Task Validate_Password_NoConfigRegex_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Password = "anypassword";
            
            _configRepositoryMock.Setup(x => x.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = null });
            _userRepositoryMock.Setup(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Password);
        }

        #endregion

        #region Email Validation Tests

        [Fact]
        public async Task Validate_Email_Null_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Email = null;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task Validate_Email_Empty_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Email = "";
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Theory]
        [InlineData("invalidemail")]
        [InlineData("@example.com")]
        [InlineData("user@")]
        [InlineData("user @example.com")]
        public async Task Validate_Email_InvalidFormat_FailsValidation(string email)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Email = email;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Email)
                .WithErrorMessage("Email invalid");
        }

        [Theory]
        [InlineData("user@example.com")]
        [InlineData("test.user@domain.co.uk")]
        [InlineData("user+tag@example.com")]
        public async Task Validate_Email_ValidFormat_PassesValidation(string email)
        {
            // Arrange
            var request = CreateValidRequest();
            request.Email = email;
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User)null);
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task Validate_Email_AlreadyInUse_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.Email = "existing@example.com";

            var existingUser = new User { ItemId = "existing-id", Email = "existing@example.com" };
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync("existing@example.com", It.IsAny<string>()))
                .ReturnsAsync(existingUser);
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync(request.UserName, It.IsAny<string>()))
                .ReturnsAsync((User)null);
            _userRepositoryMock.Setup(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _configRepositoryMock.Setup(x => x.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration 
                { 
                    PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$" 
                });

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Email)
                .WithErrorMessage("Email already in use");
        }

        #endregion

        #region PhoneNumber Validation Tests

        [Theory]
        [InlineData("1234567890")]
        [InlineData("88017********")]
        [InlineData("0017********")]
        public async Task Validate_PhoneNumber_NotStartingWithPlus_FailsValidation(string phoneNumber)
        {
            // Arrange
            var request = CreateValidRequest();
            request.PhoneNumber = phoneNumber;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.PhoneNumber)
                .WithErrorMessage("Phone number must start with a plus (+) character. E.g: +88017********");
        }

        [Theory]
        [InlineData("+1234567890")]
        [InlineData("+88017********")]
        public async Task Validate_PhoneNumber_StartingWithPlus_PassesValidation(string phoneNumber)
        {
            // Arrange
            var request = CreateValidRequest();
            request.PhoneNumber = phoneNumber;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
        }

        [Fact]
        public async Task Validate_PhoneNumber_ExceedsMaxLength_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.PhoneNumber = "+12345678901234567890123";
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.PhoneNumber)
                .WithErrorMessage("Maximum character limit 20 exceeded");
        }

        [Fact]
        public async Task Validate_PhoneNumber_Null_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.PhoneNumber = null;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
        }

        #endregion

        #region Enum Validation Tests

        [Fact]
        public async Task Validate_UserPassType_NotNull_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.UserPassType = UserPassType.Password;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.UserPassType);
        }

        [Fact]
        public async Task Validate_UserCreationType_NotNull_PassesValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.UserCreationType = UserCreationType.Portal;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.UserCreationType);
        }

        [Fact]
        public async Task Validate_UserMfaType_WhenMfaEnabled_IsRequired()
        {
            // Arrange
            var request = CreateValidRequest();
            request.MfaEnabled = true;
            request.UserMfaType = UserMfaType.TOTP;
            SetupValidMocks();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.UserMfaType);
        }

        #endregion

        #region Helper Methods

        private void SetupValidMocks()
        {
            _userRepositoryMock.Setup(x => x.GetUserByUserNameOrgIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User)null);
            _userRepositoryMock.Setup(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _configRepositoryMock.Setup(x => x.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration 
                { 
                    PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$" 
                });
        }

        #endregion
    }

    public class UpdateUserValidatorTests : IDisposable
    {
        private readonly Mock<ITenants> _tenantsMock;
        private readonly UpdateUserValidator _validator;

        public UpdateUserValidatorTests()
        {
            _tenantsMock = new Mock<ITenants>();
            _validator = new UpdateUserValidator(_tenantsMock.Object);
        }

        public void Dispose()
        {
            BlocksContext.ClearContext();
        }

        private static void SetupBlocksContext(string userId = "user-123", string tenantId = "tenant-123")
        {
            var createMethods = typeof(BlocksContext).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .ToList();

            var create15Method = createMethods.FirstOrDefault(m => m.GetParameters().Length == 15);

            if (create15Method != null)
            {
                var context = (BlocksContext)create15Method.Invoke(null, new object[]
                {
                    tenantId, Array.Empty<string>(), userId, true, string.Empty, string.Empty,
                    DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                    "testuser", string.Empty, "Test User", string.Empty, tenantId, string.Empty
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
                        tenantId, Array.Empty<string>(), userId, true, string.Empty, string.Empty,
                        DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                        "testuser", string.Empty, "Test User", string.Empty, tenantId
                    });
                    BlocksContext.SetContext(context, true);
                }
            }
        }

        private static UpdateUserRequest CreateValidRequest()
        {
            return new UpdateUserRequest
            {
                ItemId = "user-456",
                FirstName = "John",
                LastName = "Doe",
                ProjectKey = "project-123",
                Memberships = new List<OrganizationMembership>()
            };
        }

        private static Tenant CreateTestTenant(string createdBy = "creator-123")
        {
            return new Tenant
            {
                CreatedBy = createdBy,
                ApplicationDomain = "test-domain",
                DbConnectionString = "mongodb://localhost",
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = "test-pass",
                    IssueDate = DateTime.UtcNow
                }
            };
        }

        #region Complete Validation Tests

        [Fact]
        public async Task Validate_WithCompletelyValidRequest_PassesAllValidations()
        {
            // Arrange
            var userId = "user-123";
            var request = CreateValidRequest();
            request.ItemId = userId;
            
            SetupBlocksContext(userId);

            var tenant = CreateTestTenant("other-user");
            _tenantsMock.Setup(x => x.GetTenantByID(request.ProjectKey))
                .Returns(tenant);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        #endregion

        #region ItemId Validation Tests

        [Fact]
        public async Task Validate_ItemId_Null_FailsValidation()
        {
            // Arrange
            SetupBlocksContext(); // Need context even for null ItemId test
            var request = CreateValidRequest();
            request.ItemId = null;

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.ItemId);
        }

        [Fact]
        public async Task Validate_ItemId_Empty_FailsValidation()
        {
            // Arrange
            SetupBlocksContext(); // Need context even for empty ItemId test
            var request = CreateValidRequest();
            request.ItemId = "";

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.ItemId);
        }

        [Fact]
        public async Task Validate_ItemId_Valid_PassesValidation()
        {
            // Arrange
            var userId = "user-123";
            var request = CreateValidRequest();
            request.ItemId = userId;

            SetupBlocksContext(userId);
            _tenantsMock.Setup(x => x.GetTenantByID(It.IsAny<string>()))
                .Returns(CreateTestTenant());

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.ItemId);
        }

        #endregion

        #region FirstName Validation Tests

        [Theory]
        [InlineData(150)]
        [InlineData(100)]
        [InlineData(50)]
        public async Task Validate_FirstName_WithinLimit_PassesValidation(int length)
        {
            // Arrange
            var userId = "user-123";
            var request = CreateValidRequest();
            request.ItemId = userId;
            request.FirstName = new string('A', length);

            SetupBlocksContext(userId);
            _tenantsMock.Setup(x => x.GetTenantByID(It.IsAny<string>()))
                .Returns(CreateTestTenant());

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.FirstName);
        }

        [Fact]
        public async Task Validate_FirstName_ExceedsMaxLength_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.FirstName = new string('A', 151);
            
            SetupBlocksContext();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.FirstName)
                .WithErrorMessage("Maximum character limit 150 exceeded");
        }

        [Fact]
        public async Task Validate_FirstName_Null_PassesValidation()
        {
            // Arrange
            var userId = "user-123";
            var request = CreateValidRequest();
            request.ItemId = userId;
            request.FirstName = null;

            SetupBlocksContext(userId);
            _tenantsMock.Setup(x => x.GetTenantByID(It.IsAny<string>()))
                .Returns(CreateTestTenant());

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.FirstName);
        }

        #endregion

        #region LastName Validation Tests

        [Fact]
        public async Task Validate_LastName_ExceedsMaxLength_FailsValidation()
        {
            // Arrange
            var request = CreateValidRequest();
            request.LastName = new string('B', 151);
            
            SetupBlocksContext();

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.LastName)
                .WithErrorMessage("Maximum character limit 150 exceeded");
        }

        #endregion

        #region Permission Validation Tests

        [Fact]
        public async Task Validate_Permission_UserUpdatingOwnProfile_PassesValidation()
        {
            // Arrange
            var userId = "user-123";
            var request = CreateValidRequest();
            request.ItemId = userId;
            
            SetupBlocksContext(userId);
            _tenantsMock.Setup(x => x.GetTenantByID(request.ProjectKey))
                .Returns(CreateTestTenant("other-user"));

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x);
        }

        [Fact]
        public async Task Validate_Permission_TenantCreatorUpdatingUser_PassesValidation()
        {
            // Arrange
            var creatorUserId = "creator-123";
            var request = CreateValidRequest();
            request.ItemId = "other-user-456";
            
            SetupBlocksContext(creatorUserId);
            _tenantsMock.Setup(x => x.GetTenantByID(request.ProjectKey))
                .Returns(CreateTestTenant(creatorUserId));

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x);
        }

        [Fact]
        public async Task Validate_Permission_UnauthorizedUser_FailsValidation()
        {
            // Arrange
            var userId = "user-123";
            var request = CreateValidRequest();
            request.ItemId = "other-user-456";
            
            SetupBlocksContext(userId);
            _tenantsMock.Setup(x => x.GetTenantByID(request.ProjectKey))
                .Returns(CreateTestTenant("creator-789"));

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x)
                .WithErrorMessage("You don't have permission to update this user");
        }

        [Fact]
        public async Task Validate_Permission_NullTenant_FailsValidation()
        {
            // Arrange
            var userId = "user-123";
            var request = CreateValidRequest();
            request.ItemId = "other-user-456";
            
            SetupBlocksContext(userId);
            _tenantsMock.Setup(x => x.GetTenantByID(request.ProjectKey))
                .Returns((Tenant)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x)
                .WithErrorMessage("You don't have permission to update this user");
        }

        [Fact]
        public async Task Validate_Permission_EmptyProjectKey_HandlesGracefully()
        {
            // Arrange
            var userId = "user-123";
            var request = CreateValidRequest();
            request.ProjectKey = "";
            request.ItemId = "other-user-456";
            
            SetupBlocksContext(userId);
            _tenantsMock.Setup(x => x.GetTenantByID(""))
                .Returns((Tenant)null);

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task Validate_AllFieldsExceedLimits_ReturnsMultipleErrors()
        {
            // Arrange
            var request = new UpdateUserRequest
            {
                ItemId = "",
                FirstName = new string('A', 151),
                LastName = new string('B', 151),
                ProjectKey = "project-123"
            };
            
            SetupBlocksContext("user-123");
            _tenantsMock.Setup(x => x.GetTenantByID("project-123"))
                .Returns(CreateTestTenant("other-user"));

            // Act
            var result = await _validator.TestValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCountGreaterThan(1);
        }

        #endregion
    }
}
