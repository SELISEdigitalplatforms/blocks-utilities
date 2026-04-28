using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using FluentAssertions;
using FluentValidation.TestHelper;
using Iam.DomainService.Accounts;
using Iam.DomainService.Configurations;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using Moq;
using System.Linq;

namespace XUnitTest.Accounts.Validators
{
    public class ValidatorsTests : IDisposable
    {
        private readonly Mock<ICacheClient> _cacheClientMock;
        private readonly Mock<IIamConfigurationRepository> _configRepoMock;
        private readonly Mock<IIdentityAccessManagementRepository> _iamRepoMock;
        private readonly Mock<ICaptchaService> _captchaServiceMock;
        private readonly Mock<IDbContextProvider> _dbContextProviderMock;
        private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
        private readonly Mock<IMongoCollection<CaptchaConfiguration>> _captchaCollectionMock;

        public ValidatorsTests()
        {
            _cacheClientMock = new Mock<ICacheClient>();
            _configRepoMock = new Mock<IIamConfigurationRepository>();
            _iamRepoMock = new Mock<IIdentityAccessManagementRepository>();
            _captchaServiceMock = new Mock<ICaptchaService>();
            _dbContextProviderMock = new Mock<IDbContextProvider>();
            _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
            _captchaCollectionMock = new Mock<IMongoCollection<CaptchaConfiguration>>();

            // Setup default BlocksContext
            SetupBlocksContext("test-user", "test-tenant");
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

        #region BaseAccountValidator Tests

        [Fact]
        public async Task BaseAccountValidator_WithEmptyCode_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest { Code = "" };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Code);
        }

        [Fact]
        public async Task BaseAccountValidator_WithNullCode_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest { Code = null };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Code);
        }

        [Fact]
        public async Task BaseAccountValidator_WithExpiredCode_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest { Code = "expired-code" };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(false);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Code)
                .WithErrorMessage("The code has expired. Please request a new one to continue");
        }

        [Fact]
        public async Task BaseAccountValidator_WithValidCode_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest { Code = "valid-code" };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(true);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Code);
        }

        [Fact]
        public async Task BaseAccountValidator_WithWeakPassword_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest 
            { 
                Code = "valid-code", 
                Password = "weak" 
            };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(true);
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$"
            });

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Password)
                .WithErrorMessage("Password is weak. Ensure at least one lower and upper case letter, one special character, one digit and minimum 8 characters length");
        }

        [Fact]
        public async Task BaseAccountValidator_WithBlacklistedPassword_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest 
            { 
                Code = "valid-code", 
                Password = "Blacklisted1!" 
            };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(true);
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$"
            });
            _iamRepoMock.Setup(x => x.CheckPasswordBlackListedAsync(request.Password, It.IsAny<string>())).ReturnsAsync(true);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Password)
                .WithErrorMessage("This password can not be used.");
        }

        [Fact]
        public async Task BaseAccountValidator_WithStrongValidPassword_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest 
            { 
                Code = "valid-code", 
                Password = "Strong@Pass123" 
            };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(true);
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$"
            });
            _iamRepoMock.Setup(x => x.CheckPasswordBlackListedAsync(request.Password, It.IsAny<string>())).ReturnsAsync(false);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Password);
        }

        [Fact]
        public async Task BaseAccountValidator_WithEmptyPassword_ShouldNotValidatePassword()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest 
            { 
                Code = "valid-code", 
                Password = "" 
            };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(true);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Password);
            _configRepoMock.Verify(x => x.GetConfigurationAsync(), Times.Never);
        }

        [Fact]
        public async Task BaseAccountValidator_WithInvalidCaptcha_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest 
            { 
                Code = "valid-code", 
                CaptchaCode = "invalid-captcha" 
            };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(true);
            SetupCaptchaConfig("recaptcha");
            _captchaServiceMock.Setup(x => x.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = false });

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.CaptchaCode)
                .WithErrorMessage("Captcha doesn't match");
        }

        [Fact]
        public async Task BaseAccountValidator_WithValidCaptcha_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest 
            { 
                Code = "valid-code", 
                CaptchaCode = "valid-captcha" 
            };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(true);
            SetupCaptchaConfig("recaptcha");
            _captchaServiceMock.Setup(x => x.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = true });

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.CaptchaCode);
        }

        [Fact]
        public async Task BaseAccountValidator_WithEmptyCaptcha_ShouldNotValidateCaptcha()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest 
            { 
                Code = "valid-code", 
                CaptchaCode = "" 
            };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(true);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.CaptchaCode);
            _captchaServiceMock.Verify(x => x.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()), Times.Never);
        }

        [Fact]
        public async Task BaseAccountValidator_WithNullCaptchaConfig_UsesEmptyProvider()
        {
            // Arrange
            var validator = CreateBaseAccountValidator();
            var request = new BaseAccountRequest 
            { 
                Code = "valid-code", 
                CaptchaCode = "captcha-code" 
            };
            _cacheClientMock.Setup(x => x.KeyExistsAsync(request.Code)).ReturnsAsync(true);
            SetupCaptchaConfig(null); // No config found
            _captchaServiceMock.Setup(x => x.VerifyCaptchaAsync(It.Is<VerifyCaptchaRequest>(r => r.ConfigurationName == "")))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = true });

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            _captchaServiceMock.Verify(x => x.VerifyCaptchaAsync(It.Is<VerifyCaptchaRequest>(r => r.ConfigurationName == "")), Times.Once);
        }

        #endregion

        #region ChangePasswordValidator Tests

        [Fact]
        public async Task ChangePasswordValidator_WithEmptyOldPassword_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "", NewPassword = "Strong@Pass123" };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.OldPassword);
        }

        [Fact]
        public async Task ChangePasswordValidator_WithNullOldPassword_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = null, NewPassword = "Strong@Pass123" };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.OldPassword);
        }

        [Fact]
        public async Task ChangePasswordValidator_WithEmptyNewPassword_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "OldPass123!", NewPassword = "" };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.NewPassword);
        }

        [Fact]
        public async Task ChangePasswordValidator_WithNullNewPassword_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "OldPass123!", NewPassword = null };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.NewPassword);
        }

        [Fact]
        public async Task ChangePasswordValidator_WithWeakNewPassword_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "OldPass123!", NewPassword = "weak" };
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$"
            });

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.NewPassword)
                .WithErrorMessage("Password weak. Ensure at least one lower and upper case letter, one special character, one digit and minimum 8 characters length");
        }

        [Fact]
        public async Task ChangePasswordValidator_WithBlacklistedNewPassword_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "OldPass123!", NewPassword = "Blacklisted1!" };
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$"
            });
            _iamRepoMock.Setup(x => x.CheckPasswordBlackListedAsync(request.NewPassword, It.IsAny<string>())).ReturnsAsync(true);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.NewPassword)
                .WithErrorMessage("This password can not be used.");
        }

        [Fact]
        public async Task ChangePasswordValidator_WithValidPasswords_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "OldPass123!", NewPassword = "NewStrong@Pass123" };
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$"
            });
            _iamRepoMock.Setup(x => x.CheckPasswordBlackListedAsync(request.NewPassword, It.IsAny<string>())).ReturnsAsync(false);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveAnyValidationErrors();
        }

        #endregion

        #region PasswordValidator Tests (via ChangePasswordValidator)

        [Fact]
        public async Task PasswordValidator_WithNullConfigAndNullRegex_ShouldAllowAnyPassword()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "old", NewPassword = "any" };
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync((IamConfiguration)null);
            _iamRepoMock.Setup(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.NewPassword);
        }

        [Fact]
        public async Task PasswordValidator_WithEmptyRegex_ShouldAllowAnyPassword()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "old", NewPassword = "any" };
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = ""
            });
            _iamRepoMock.Setup(x => x.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.NewPassword);
        }

        [Fact]
        public async Task PasswordValidator_WithRegexTimeout_ShouldRejectPassword()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "old", NewPassword = "test" };
            // This regex will cause timeout due to catastrophic backtracking
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(a+)+$" // Catastrophic backtracking pattern
            });

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert - The timeout should cause validation to fail
            result.ShouldHaveValidationErrorFor(x => x.NewPassword);
        }

        [Fact]
        public async Task PasswordValidator_CheckBlackListPassword_WithBlacklistedPassword_ReturnsFalse()
        {
            // Arrange
            var validator = CreateChangePasswordValidator();
            var request = new ChangePasswordRequest { OldPassword = "old", NewPassword = "Strong@Pass123" };
            _configRepoMock.Setup(x => x.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
            {
                PasswordStrengthCheckerRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$"
            });
            _iamRepoMock.Setup(x => x.CheckPasswordBlackListedAsync(request.NewPassword, "test-tenant")).ReturnsAsync(true);

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.NewPassword);
            _iamRepoMock.Verify(x => x.CheckPasswordBlackListedAsync(request.NewPassword, "test-tenant"), Times.Once);
        }

        #endregion

        #region RecoveryUserRequestValidator Tests

        [Fact]
        public async Task RecoveryUserRequestValidator_WithEmptyEmail_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateRecoveryUserRequestValidator();
            var request = new RecoveryUserRequest { Email = "" };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Email)
                .WithErrorMessage("Email is required.");
        }

        [Fact]
        public async Task RecoveryUserRequestValidator_WithInvalidEmailFormat_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateRecoveryUserRequestValidator();
            var request = new RecoveryUserRequest { Email = "invalid-email" };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.Email)
                .WithErrorMessage("Invalid email format.");
        }

        [Fact]
        public async Task RecoveryUserRequestValidator_WithValidEmail_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = CreateRecoveryUserRequestValidator();
            var request = new RecoveryUserRequest { Email = "test@example.com" };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task RecoveryUserRequestValidator_WithInvalidCaptcha_ShouldHaveValidationError()
        {
            // Arrange
            var validator = CreateRecoveryUserRequestValidator();
            var request = new RecoveryUserRequest 
            { 
                Email = "test@example.com", 
                CaptchaCode = "invalid-captcha" 
            };
            SetupCaptchaConfig("recaptcha");
            _captchaServiceMock.Setup(x => x.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = false });

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldHaveValidationErrorFor(x => x.CaptchaCode)
                .WithErrorMessage("Captcha doesn't match");
        }

        [Fact]
        public async Task RecoveryUserRequestValidator_WithValidCaptcha_ShouldNotHaveValidationError()
        {
            // Arrange
            var validator = CreateRecoveryUserRequestValidator();
            var request = new RecoveryUserRequest 
            { 
                Email = "test@example.com", 
                CaptchaCode = "valid-captcha" 
            };
            SetupCaptchaConfig("recaptcha");
            _captchaServiceMock.Setup(x => x.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = true });

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public async Task RecoveryUserRequestValidator_WithEmptyCaptcha_ShouldNotValidateCaptcha()
        {
            // Arrange
            var validator = CreateRecoveryUserRequestValidator();
            var request = new RecoveryUserRequest 
            { 
                Email = "test@example.com", 
                CaptchaCode = "" 
            };

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            result.ShouldNotHaveValidationErrorFor(x => x.CaptchaCode);
            _captchaServiceMock.Verify(x => x.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()), Times.Never);
        }

        [Fact]
        public async Task RecoveryUserRequestValidator_WithNullCaptchaConfig_UsesEmptyProvider()
        {
            // Arrange
            var validator = CreateRecoveryUserRequestValidator();
            var request = new RecoveryUserRequest 
            { 
                Email = "test@example.com", 
                CaptchaCode = "captcha-code" 
            };
            SetupCaptchaConfig(null);
            _captchaServiceMock.Setup(x => x.VerifyCaptchaAsync(It.Is<VerifyCaptchaRequest>(r => r.ConfigurationName == "")))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = true });

            // Act
            var result = await validator.TestValidateAsync(request);

            // Assert
            _captchaServiceMock.Verify(x => x.VerifyCaptchaAsync(It.Is<VerifyCaptchaRequest>(r => r.ConfigurationName == "")), Times.Once);
        }

        #endregion

        #region Helper Methods

        private BaseAccountValidator CreateBaseAccountValidator()
        {
            return new BaseAccountValidator(
                _cacheClientMock.Object,
                _configRepoMock.Object,
                _iamRepoMock.Object,
                _captchaServiceMock.Object,
                _dbContextProviderMock.Object
            );
        }

        private ChangePasswordValidator CreateChangePasswordValidator()
        {
            return new ChangePasswordValidator(
                _configRepoMock.Object,
                _iamRepoMock.Object
            );
        }

        private RecoveryUserRequestValidator CreateRecoveryUserRequestValidator()
        {
            return new RecoveryUserRequestValidator(
                _captchaServiceMock.Object,
                _dbContextProviderMock.Object
            );
        }

        private void SetupCaptchaConfig(string provider)
        {
            var captchaConfig = provider != null ? new CaptchaConfiguration { Provider = provider, IsEnable = true } : null;
            
            var asyncCursorMock = new Mock<IAsyncCursor<CaptchaConfiguration>>();
            asyncCursorMock.Setup(x => x.Current).Returns(captchaConfig != null ? new[] { captchaConfig } : Array.Empty<CaptchaConfiguration>());
            asyncCursorMock.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(captchaConfig != null)
                .Returns(false);
            asyncCursorMock.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(captchaConfig != null)
                .ReturnsAsync(false);

            _captchaCollectionMock.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<CaptchaConfiguration>>(),
                It.IsAny<FindOptions<CaptchaConfiguration, CaptchaConfiguration>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(asyncCursorMock.Object);

            _dbContextProviderMock.Setup(x => x.GetCollection<CaptchaConfiguration>("CaptchaConfigurations"))
                .Returns(_captchaCollectionMock.Object);
        }

        #endregion
    }
}
