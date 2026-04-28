using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;
using Microsoft.Extensions.Logging;
using Moq;
using Blocks.Genesis;
using System.Linq;

namespace XUnitTest.Accounts
{
    public class AccountServiceTests : IDisposable
    {
        private readonly Mock<ILogger<AccountService>> _loggerMock;
        private readonly Mock<IIdentityAccessManagementRepository> _repositoryMock;
        private readonly Mock<IIdentityAccessManagementService> _iamServiceMock;
        private readonly Mock<ICacheClient> _cacheClientMock;
        private readonly Mock<IValidator<BaseAccountRequest>> _accountValidatorMock;
        private readonly Mock<IValidator<ChangePasswordRequest>> _changePasswordValidatorMock;
        private readonly Mock<IValidator<RecoveryUserRequest>> _recoverUserValidatorMock;
        private readonly AccountService _accountService;

        public AccountServiceTests()
        {
            _loggerMock = new Mock<ILogger<AccountService>>();
            _repositoryMock = new Mock<IIdentityAccessManagementRepository>();
            _iamServiceMock = new Mock<IIdentityAccessManagementService>();
            _cacheClientMock = new Mock<ICacheClient>();
            _accountValidatorMock = new Mock<IValidator<BaseAccountRequest>>();
            _changePasswordValidatorMock = new Mock<IValidator<ChangePasswordRequest>>();
            _recoverUserValidatorMock = new Mock<IValidator<RecoveryUserRequest>>();

            _accountService = new AccountService(
                _loggerMock.Object,
                _repositoryMock.Object,
                _iamServiceMock.Object,
                _cacheClientMock.Object,
                _accountValidatorMock.Object,
                _changePasswordValidatorMock.Object,
                _recoverUserValidatorMock.Object
            );
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        private static void SetupBlocksContext(string userId, string tenantId)
        {
            // Get all Create methods and find the one we can use
            var createMethods = typeof(BlocksContext).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .ToList();

            // Try 15-parameter version first (newer API with actualTentId + refreshToken)
            var create15Method = createMethods.FirstOrDefault(m => m.GetParameters().Length == 15);

            if (create15Method != null)
            {
                // Use 15 parameters
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
                // Try 14-parameter version (older API without refreshToken)
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
                else
                {
                    throw new InvalidOperationException("Could not find a compatible BlocksContext.Create method.");
                }
            }
        }

        #region ActivateAccountAsync Tests

        [Fact]
        public async Task ActivateAccountAsync_WithInvalidRequest_ReturnsValidationErrors()
        {
            // Arrange
            var request = new ActivateUserRequest();
            var validationResult = new ValidationResult(new List<ValidationFailure>
            {
                new ValidationFailure("Code", "Code is required")
            });
            _accountValidatorMock.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(validationResult);

            // Act
            var result = await _accountService.ActivateAccountAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().ContainKey("Code");
            result.Errors["Code"].Should().Be("Code is required");
        }

        [Fact]
        public async Task ActivateAccountAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = new ActivateUserRequest { Code = "test-code", Password = "NewPass123!" };
            var userId = "user-123";
            var user = new User { ItemId = userId, Active = false, IsVarified = false };

            _accountValidatorMock.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(new ValidationResult());
            _cacheClientMock.Setup(x => x.GetStringValueAsync(request.Code)).ReturnsAsync(userId);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _iamServiceMock.Setup(x => x.HashPassword(request.Password)).Returns("hashed-password");
            _repositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _cacheClientMock.Setup(x => x.RemoveKeyAsync(request.Code)).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<AccountActivityEvent>())).Returns(Task.CompletedTask);

            // Act
            var result = await _accountService.ActivateAccountAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _repositoryMock.Verify(x => x.UpdateUserAsync(It.Is<User>(u => u.Active && u.IsVarified)), Times.Once);
        }

        [Fact]
        public async Task ProcessActivationAsync_WithNonExistentUser_ReturnsFalse()
        {
            // Arrange
            var request = new ActivateUserRequest { Code = "test-code" };
            _cacheClientMock.Setup(x => x.GetStringValueAsync(request.Code)).ReturnsAsync("user-123");
            _repositoryMock.Setup(x => x.GetUserByIdAsync("user-123")).ReturnsAsync((User)null);

            // Act
            var result = await _accountService.ProcessActivationAsync(request);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessActivationAsync_WithoutPassword_ActivatesUserWithoutUpdatingPassword()
        {
            // Arrange
            var request = new ActivateUserRequest { Code = "test-code", Password = "" };
            var user = new User { ItemId = "user-123", Active = false, Password = "old-password" };
            _cacheClientMock.Setup(x => x.GetStringValueAsync(request.Code)).ReturnsAsync(user.ItemId);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _cacheClientMock.Setup(x => x.RemoveKeyAsync(request.Code)).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<AccountActivityEvent>())).Returns(Task.CompletedTask);

            // Act
            var result = await _accountService.ProcessActivationAsync(request);

            // Assert
            result.Should().BeTrue();
            user.Password.Should().Be("old-password");
            _iamServiceMock.Verify(x => x.HashPassword(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region RecoverAccountAsync Tests

        [Fact]
        public async Task RecoverAccountAsync_WithInvalidRequest_ReturnsValidationErrors()
        {
            // Arrange
            var request = new RecoveryUserRequest();
            var validationResult = new ValidationResult(new List<ValidationFailure>
            {
                new ValidationFailure("Email", "Email is required")
            });
            _recoverUserValidatorMock.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(validationResult);

            // Act
            var result = await _accountService.RecoverAccountAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Email");
        }

        [Fact]
        public async Task RecoverAccountAsync_WithNonExistentUser_ReturnsNotAllowedError()
        {
            // Arrange
            var request = new RecoveryUserRequest { Email = "test@example.com" };
            _recoverUserValidatorMock.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(x => x.GetUserByEmailAsync(request.Email)).ReturnsAsync((User)null);

            // Act
            var result = await _accountService.RecoverAccountAsync(request);

            // Assert
            result.Errors.Should().ContainKey("Email");
            result.Errors["Email"].Should().Be("Not_Allowed");
        }

        [Fact]
        public async Task RecoverAccountAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = new RecoveryUserRequest { Email = "test@example.com", MailPurpose = "Recovery", ProjectKey = "project-1" };
            var user = new User { ItemId = "user-123", Email = request.Email, Language = "en-US", FirstName = "John", LastName = "Doe" };
            var config = new IamConfiguration { RecoverAccountUrl = "https://example.com/recover", RecoverAccountUrlLifetimeInMinutes = 30 };

            _recoverUserValidatorMock.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(x => x.GetUserByEmailAsync(request.Email)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.GetIamConfigurationAsync()).ReturnsAsync(config);
            _cacheClientMock.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), user.ItemId, It.IsAny<int>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<SendMail>())).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);

            // Act
            var result = await _accountService.RecoverAccountAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task ProcessRecoverAccountAsync_WithEmptyMailPurpose_UsesDefaultRecoverAccount()
        {
            // Arrange
            var user = new User { ItemId = "user-123", Email = "test@example.com", Language = "en-US", FirstName = "John", LastName = "Doe" };
            var config = new IamConfiguration { RecoverAccountUrl = "https://example.com/recover", RecoverAccountUrlLifetimeInMinutes = 30 };
            _repositoryMock.Setup(x => x.GetIamConfigurationAsync()).ReturnsAsync(config);
            _cacheClientMock.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), user.ItemId, It.IsAny<int>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<SendMail>())).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);

            // Act
            var result = await _accountService.ProcessRecoverAccountAsync(user, "", "project-1");

            // Assert
            result.Should().BeTrue();
            _iamServiceMock.Verify(x => x.SendEmailAsync(It.Is<SendMail>(m => m.Purpose == "RecoverAccount")), Times.Once);
        }

        #endregion

        #region ResetAccountPasswordAsync Tests

        [Fact]
        public async Task ResetAccountPasswordAsync_WithInvalidRequest_ReturnsValidationErrors()
        {
            // Arrange
            var request = new ResetPasswordRequest();
            var validationResult = new ValidationResult(new List<ValidationFailure>
            {
                new ValidationFailure("Password", "Password is required")
            });
            _accountValidatorMock.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(validationResult);

            // Act
            var result = await _accountService.ResetAccountPasswordAsync(request);

            // Assert
            result.Errors.Should().ContainKey("Password");
        }

        [Fact]
        public async Task ResetAccountPasswordAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = new ResetPasswordRequest { Code = "reset-code", Password = "NewPass123!", LogoutFromAllDevices = true };
            var user = new User { ItemId = "user-123" };

            _accountValidatorMock.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(new ValidationResult());
            _cacheClientMock.Setup(x => x.GetStringValueAsync(request.Code)).ReturnsAsync(user.ItemId);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _iamServiceMock.Setup(x => x.HashPassword(request.Password)).Returns("hashed-password");
            _repositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _cacheClientMock.Setup(x => x.RemoveKeyAsync(request.Code)).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<AccountActivityEvent>())).Returns(Task.CompletedTask);

            // Act
            var result = await _accountService.ResetAccountPasswordAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task ProcessResetPasswordAsync_WithNonExistentUser_ReturnsFalse()
        {
            // Arrange
            var request = new ResetPasswordRequest { Code = "reset-code", Password = "NewPass123!" };
            _cacheClientMock.Setup(x => x.GetStringValueAsync(request.Code)).ReturnsAsync("user-123");
            _repositoryMock.Setup(x => x.GetUserByIdAsync("user-123")).ReturnsAsync((User)null);

            // Act
            var result = await _accountService.ProcessResetPasswordAsync(request);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessResetPasswordAsync_WithLogoutFromAllDevicesFalse_SendsPreventPostEventTrue()
        {
            // Arrange
            var request = new ResetPasswordRequest { Code = "reset-code", Password = "NewPass123!", LogoutFromAllDevices = false };
            var user = new User { ItemId = "user-123" };
            _cacheClientMock.Setup(x => x.GetStringValueAsync(request.Code)).ReturnsAsync(user.ItemId);
            _repositoryMock.Setup(x => x.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _iamServiceMock.Setup(x => x.HashPassword(request.Password)).Returns("hashed-password");
            _repositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _cacheClientMock.Setup(x => x.RemoveKeyAsync(request.Code)).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<AccountActivityEvent>())).Returns(Task.CompletedTask);

            // Act
            var result = await _accountService.ProcessResetPasswordAsync(request);

            // Assert
            result.Should().BeTrue();
            _iamServiceMock.Verify(x => x.SendToQueueAsync(It.IsAny<string>(), It.Is<AccountActivityEvent>(e => e.PreventPostEvent)), Times.Once);
        }

        #endregion

        #region ChangePasswordAsync Tests

        [Fact]
        public async Task ChangePasswordAsync_WithInvalidRequest_ReturnsValidationErrors()
        {
            // Arrange
            var request = new ChangePasswordRequest();
            var validationResult = new ValidationResult(new List<ValidationFailure>
            {
                new ValidationFailure("OldPassword", "OldPassword is required")
            });
            _changePasswordValidatorMock.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(validationResult);

            // Act
            var result = await _accountService.ChangePasswordAsync(request);

            // Assert
            result.Errors.Should().ContainKey("OldPassword");
        }

        [Fact]
        public async Task ChangePasswordAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = new ChangePasswordRequest { OldPassword = "OldPass123!", NewPassword = "NewPass123!" };
            var user = new User { ItemId = "user-123", Password = "hashed-old-password" };
            var config = new IamConfiguration { LogoutOnPasswordChange = true };

            SetupBlocksContext(user.ItemId, "test-tenant");

            _changePasswordValidatorMock.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(new ValidationResult());
            _repositoryMock.Setup(x => x.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _iamServiceMock.Setup(x => x.HashPassword(request.OldPassword)).Returns("hashed-old-password");
            _iamServiceMock.Setup(x => x.HashPassword(request.NewPassword)).Returns("hashed-new-password");
            _repositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.GetIamConfigurationAsync()).ReturnsAsync(config);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<AccountActivityEvent>())).Returns(Task.CompletedTask);

            // Act
            var result = await _accountService.ChangePasswordAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task ProcessChangePasswordAsync_WithNonExistentUser_ReturnsFalse()
        {
            // Arrange
            var request = new ChangePasswordRequest { OldPassword = "OldPass123!", NewPassword = "NewPass123!" };
            SetupBlocksContext("user-123", "test-tenant");
            _repositoryMock.Setup(x => x.GetUserByIdAsync("user-123")).ReturnsAsync((User)null);

            // Act
            var result = await _accountService.ProcessChangePasswordAsync(request);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessChangePasswordAsync_WithIncorrectOldPassword_ReturnsFalse()
        {
            // Arrange
            var request = new ChangePasswordRequest { OldPassword = "WrongPass123!", NewPassword = "NewPass123!" };
            var user = new User { ItemId = "user-123", Password = "hashed-old-password" };
            SetupBlocksContext(user.ItemId, "test-tenant");
            _repositoryMock.Setup(x => x.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _iamServiceMock.Setup(x => x.HashPassword(request.OldPassword)).Returns("different-hash");

            // Act
            var result = await _accountService.ProcessChangePasswordAsync(request);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessChangePasswordAsync_WithLogoutOnPasswordChangeFalse_SendsPreventPostEventTrue()
        {
            // Arrange
            var request = new ChangePasswordRequest { OldPassword = "OldPass123!", NewPassword = "NewPass123!" };
            var user = new User { ItemId = "user-123", Password = "hashed-old-password" };
            var config = new IamConfiguration { LogoutOnPasswordChange = false };

            SetupBlocksContext(user.ItemId, "test-tenant");
            _repositoryMock.Setup(x => x.GetUserByIdAsync(user.ItemId)).ReturnsAsync(user);
            _iamServiceMock.Setup(x => x.HashPassword(request.OldPassword)).Returns("hashed-old-password");
            _iamServiceMock.Setup(x => x.HashPassword(request.NewPassword)).Returns("hashed-new-password");
            _repositoryMock.Setup(x => x.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.GetIamConfigurationAsync()).ReturnsAsync(config);
            _iamServiceMock.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<AccountActivityEvent>())).Returns(Task.CompletedTask);

            // Act
            var result = await _accountService.ProcessChangePasswordAsync(request);

            // Assert
            result.Should().BeTrue();
            _iamServiceMock.Verify(x => x.SendToQueueAsync(It.IsAny<string>(), It.Is<AccountActivityEvent>(e => e.PreventPostEvent)), Times.Once);
        }

        #endregion

        #region ResendActivationAsync Tests

        [Fact]
        public async Task ResendActivationAsync_WithEmptyUserId_ReturnsError()
        {
            // Arrange
            var request = new ResendActivationRequest { UserId = "" };

            // Act
            var result = await _accountService.ResendActivationAsync(request);

            // Assert
            result.Errors.Should().ContainKey("UserId");
            result.Errors["UserId"].Should().Be("UserId_Required");
        }

        [Fact]
        public async Task ResendActivationAsync_WithNonExistentUser_ReturnsEmptyResponse()
        {
            // Arrange
            var request = new ResendActivationRequest { UserId = "user-123" };
            _repositoryMock.Setup(x => x.GetUserByIdAsync(request.UserId)).ReturnsAsync((User)null);

            // Act
            var result = await _accountService.ResendActivationAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().BeNull();
        }

        [Fact]
        public async Task ResendActivationAsync_WithValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = new ResendActivationRequest { UserId = "user-123", ProjectKey = "project-1" };
            var user = new User { ItemId = request.UserId, Email = "test@example.com", Language = "en-US", FirstName = "John", LastName = "Doe" };
            var config = new IamConfiguration { AccountActivationUrl = "https://example.com/activate", ActivationUrlLifetimeInMinutes = 30 };

            _repositoryMock.Setup(x => x.GetUserByIdAsync(request.UserId)).ReturnsAsync(user);
            _repositoryMock.Setup(x => x.GetIamConfigurationAsync()).ReturnsAsync(config);
            _cacheClientMock.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), user.ItemId, It.IsAny<int>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);

            // Act
            var result = await _accountService.ResendActivationAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task SendReActivationAsync_WithEmptyMailPurpose_UsesDefaultAccountActivation()
        {
            // Arrange
            var user = new User { ItemId = "user-123", Email = "test@example.com", Language = "en-US", FirstName = "John", MailPurpose = "" };
            var config = new IamConfiguration { AccountActivationUrl = "https://example.com/activate", ActivationUrlLifetimeInMinutes = 30 };

            _repositoryMock.Setup(x => x.GetIamConfigurationAsync()).ReturnsAsync(config);
            _cacheClientMock.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), user.ItemId, It.IsAny<int>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);

            // Act
            var result = await _accountService.SendReActivationAsync(user, "project-1");

            // Assert
            result.Should().BeTrue();
            _iamServiceMock.Verify(x => x.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), "AccountActivation", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task SendReActivationAsync_WithMailPurpose_UsesUserMailPurpose()
        {
            // Arrange
            var user = new User { ItemId = "user-123", Email = "test@example.com", Language = "en-US", MailPurpose = "CustomPurpose" };
            var config = new IamConfiguration { AccountActivationUrl = "https://example.com/activate", ActivationUrlLifetimeInMinutes = 30 };

            _repositoryMock.Setup(x => x.GetIamConfigurationAsync()).ReturnsAsync(config);
            _cacheClientMock.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), user.ItemId, It.IsAny<int>())).ReturnsAsync(true);
            _iamServiceMock.Setup(x => x.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _repositoryMock.Setup(x => x.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);

            // Act
            var result = await _accountService.SendReActivationAsync(user, "project-1");

            // Assert
            result.Should().BeTrue();
            _iamServiceMock.Verify(x => x.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), "CustomPurpose", It.IsAny<string>()), Times.Once);
        }

        #endregion

        #region ValidateAccountActivationCodeAsync Tests

        [Fact]
        public async Task ValidateAccountActivationCodeAsync_WithEmptyCode_ReturnsError()
        {
            // Arrange
            var request = new ValidateActivationCodeRequest { ActivationCode = "" };

            // Act
            var result = await _accountService.ValidateAccountActivationCodeAsync(request);

            // Assert
            result.Errors.Should().ContainKey("ActivationCode");
            result.Errors["ActivationCode"].Should().Be("ActivationCode_Required");
        }

        [Fact]
        public async Task ValidateAccountActivationCodeAsync_WithExistingCodeInCache_ReturnsSuccess()
        {
            // Arrange
            var request = new ValidateActivationCodeRequest { ActivationCode = "valid-code" };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.ActivationCode)).ReturnsAsync(true);

            // Act
            var result = await _accountService.ValidateAccountActivationCodeAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task ValidateAccountActivationCodeAsync_WithCodeInDatabase_ReturnsSuccessWithUserId()
        {
            // Arrange
            var request = new ValidateActivationCodeRequest { ActivationCode = "db-code" };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.ActivationCode)).ReturnsAsync(false);
            _repositoryMock.Setup(x => x.GetUserIdFromKeyMapByKeyAsync(request.ActivationCode)).ReturnsAsync("user-123");

            // Act
            var result = await _accountService.ValidateAccountActivationCodeAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.UserId.Should().Be("user-123");
        }

        [Fact]
        public async Task ValidateAccountActivationCodeAsync_WithInvalidCode_ReturnsError()
        {
            // Arrange
            var request = new ValidateActivationCodeRequest { ActivationCode = "invalid-code" };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.ActivationCode)).ReturnsAsync(false);
            _repositoryMock.Setup(x => x.GetUserIdFromKeyMapByKeyAsync(request.ActivationCode)).ReturnsAsync((string)null);

            // Act
            var result = await _accountService.ValidateAccountActivationCodeAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ActivationCode");
            result.Errors["ActivationCode"].Should().Be("Invalid_ActivationCode");
        }

        #endregion

        #region SaveSingUpSettingAsync Tests

        [Fact]
        public async Task SaveSingUpSettingAsync_WithNewSettingWhenAlreadyExists_ReturnsError()
        {
            // Arrange
            var request = new SaveSignUpSettingRequest { ItemId = "", IsEmailPasswordSignUpEnabled = true };
            _repositoryMock.Setup(x => x.SingnUpSettingAlreadyExist()).ReturnsAsync(true);

            // Act
            var result = await _accountService.SaveSingUpSettingAsync(request);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("sing_up_setting_exist");
        }

        [Fact]
        public async Task SaveSingUpSettingAsync_WithNewSetting_CreatesAndReturnsSuccess()
        {
            // Arrange
            var request = new SaveSignUpSettingRequest 
            { 
                ItemId = "", 
                IsEmailPasswordSignUpEnabled = true, 
                IsSSoSignUpEnabled = false 
            };
            SetupBlocksContext("admin-123", "test-tenant");
            _repositoryMock.Setup(x => x.SingnUpSettingAlreadyExist()).ReturnsAsync(false);
            _repositoryMock.Setup(x => x.SaveSingUpSettingAsync(It.IsAny<SignUpSetting>())).Returns(Task.CompletedTask);

            // Act
            var result = await _accountService.SaveSingUpSettingAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrEmpty();
            _repositoryMock.Verify(x => x.SaveSingUpSettingAsync(It.Is<SignUpSetting>(s => 
                s.IsEmailPasswordSignUpEnabled == true && 
                s.IsSSoSignUpEnabled == false &&
                s.CreatedBy == "admin-123")), Times.Once);
        }

        [Fact]
        public async Task SaveSingUpSettingAsync_WithExistingItemId_UpdatesAndReturnsSuccess()
        {
            // Arrange
            var existingId = Guid.NewGuid().ToString();
            var request = new SaveSignUpSettingRequest 
            { 
                ItemId = existingId, 
                IsEmailPasswordSignUpEnabled = false, 
                IsSSoSignUpEnabled = true 
            };
            var existingSetting = new SignUpSetting 
            { 
                ItemId = existingId, 
                IsEmailPasswordSignUpEnabled = true,
                CreatedBy = "original-user",
                CreatedDate = DateTime.UtcNow.AddDays(-1)
            };
            SetupBlocksContext("admin-123", "test-tenant");
            _repositoryMock.Setup(x => x.GetSingUpSettingByIdAsync(existingId)).ReturnsAsync(existingSetting);
            _repositoryMock.Setup(x => x.SaveSingUpSettingAsync(It.IsAny<SignUpSetting>())).Returns(Task.CompletedTask);

            // Act
            var result = await _accountService.SaveSingUpSettingAsync(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be(existingId);
            _repositoryMock.Verify(x => x.SaveSingUpSettingAsync(It.Is<SignUpSetting>(s => 
                s.ItemId == existingId &&
                s.IsEmailPasswordSignUpEnabled == false && 
                s.IsSSoSignUpEnabled == true &&
                s.LastUpdatedBy == "admin-123")), Times.Once);
        }

        #endregion

        #region GetSignUpSettingAsync Tests

        [Fact]
        public async Task GetSignUpSettingAsync_ReturnsSettingFromRepository()
        {
            // Arrange
            var request = new GetSignUpSettingRequest { ItemId = "setting-123" };
            var expectedSetting = new SignUpSetting 
            { 
                ItemId = request.ItemId, 
                IsEmailPasswordSignUpEnabled = true 
            };
            _repositoryMock.Setup(x => x.GetSignUpSettingAsync(request.ItemId)).ReturnsAsync(expectedSetting);

            // Act
            var result = await _accountService.GetSignUpSettingAsync(request);

            // Assert
            result.Should().BeSameAs(expectedSetting);
        }

        #endregion

        #region SendActivationToEmailAsync Tests

        [Fact]
        public async Task SendActivationToEmailAsync_SendsEmailWithCorrectParameters()
        {
            // Arrange
            var user = new User 
            { 
                ItemId = "user-123", 
                Email = "TEST@EXAMPLE.COM", 
                FirstName = "John", 
                LastName = "Doe",
                Language = "fr-FR"
            };
            var url = "https://example.com/activate?code=abc123";
            var purpose = "AccountActivation";
            var projectKey = "project-1";

            _iamServiceMock.Setup(x => x.SendEmailAsync(It.IsAny<SendMail>())).ReturnsAsync(true);

            // Act
            var result = await _accountService.SendActivationToEmailAsync(user, url, purpose, projectKey);

            // Assert
            result.Should().BeTrue();
            _iamServiceMock.Verify(x => x.SendEmailAsync(It.Is<SendMail>(m =>
                m.To.Contains("test@example.com") &&
                m.Language == "fr-FR" &&
                m.Purpose == purpose &&
                m.ProjectKey == projectKey &&
                m.BodyDataContext["User.DisplayName"] == "John Doe" &&
                m.BodyDataContext["EmailVerification.PageUrl"] == url
            )), Times.Once);
        }

        #endregion
    }
}
