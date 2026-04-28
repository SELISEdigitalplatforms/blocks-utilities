using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Moq;
using MfaConfig = Mfa.DomainService.Configuration.Configuration;

namespace XUnitTest.DomainService.Mfa
{
    public class MfaManagementServiceTests
    {
        private readonly Mock<IOtpServiceFactory> _otpServiceFactory;
        private readonly Mock<IMfaManagementRepository> _mfaRepository;
        private readonly Mock<IMfaConfigurationService> _configurationService;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly MfaManagementService _service;

        public MfaManagementServiceTests()
        {
            _otpServiceFactory = new Mock<IOtpServiceFactory>();
            _mfaRepository = new Mock<IMfaManagementRepository>();
            _configurationService = new Mock<IMfaConfigurationService>();
            _cacheClient = new Mock<ICacheClient>();

            _service = new MfaManagementService(
                _otpServiceFactory.Object,
                _mfaRepository.Object,
                _configurationService.Object,
                _cacheClient.Object);
        }

        #region GenerateOTPAsync

        [Fact]
        public async Task GenerateOTPAsync_WhenMfaNotEnabled_ReturnsErrorResponse()
        {
            // Arrange
            var request = new OtpGenerationRequest { UserId = "user-123" };
            _configurationService.Setup(x => x.GetAsync())
                .ReturnsAsync((MfaConfig)null);

            // Act
            var result = await _service.GenerateOTPAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("mfa_not_enable");
            result.Errors["mfa_not_enable"].Should().Be("Please enable mfa for your application first");
        }

        [Fact]
        public async Task GenerateOTPAsync_WhenMfaEnabledIsFalse_ReturnsErrorResponse()
        {
            // Arrange
            var request = new OtpGenerationRequest { UserId = "user-123" };
            var config = new MfaConfig { EnableMfa = false };
            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(config);

            // Act
            var result = await _service.GenerateOTPAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("mfa_not_enable");
        }

        [Fact]
        public async Task GenerateOTPAsync_WhenUserIdIsEmpty_ReturnsErrorResponse()
        {
            // Arrange
            var request = new OtpGenerationRequest { UserId = "" };
            var config = new MfaConfig { EnableMfa = true };
            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(config);

            // Act
            var result = await _service.GenerateOTPAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().ContainKey("empty_user_id");
            result.Errors["empty_user_id"].Should().Be("Mfa is not enable for this user");
        }

        [Fact]
        public async Task GenerateOTPAsync_WithValidRequest_GeneratesOTP()
        {
            // Arrange
            var request = new OtpGenerationRequest 
            { 
                UserId = "user-123", 
                MfaType = UserMfaType.Email,
                SendPhoneNumberAsEmailDomain = "example.com"
            };
            var config = new MfaConfig { EnableMfa = true };
            var userInfo = new UserInfo 
            { 
                ItemId = "user-123",
                Email = "test@example.com",
                UserMfaType = UserMfaType.Email 
            };
            var expectedResponse = new OtpGenerationResponse 
            { 
                IsSuccess = true, 
                MfaId = "mfa-123" 
            };
            var mockOtpService = new Mock<IOtpService>();

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(config);
            _mfaRepository.Setup(x => x.GetItemAsync<UserInfo>(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserInfo, bool>>>(), 
                "Users"))
                .ReturnsAsync(userInfo);
            _otpServiceFactory.Setup(x => x.GetOTPService(UserMfaType.Email))
                .Returns(mockOtpService.Object);
            mockOtpService.Setup(x => x.GenerateAsync(userInfo, "example.com"))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _service.GenerateOTPAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().Be("mfa-123");
            mockOtpService.Verify(x => x.GenerateAsync(userInfo, "example.com"), Times.Once);
        }

        [Fact]
        public async Task GenerateOTPAsync_WithoutMfaType_UsesUserMfaType()
        {
            // Arrange
            var request = new OtpGenerationRequest 
            { 
                UserId = "user-123",
                SendPhoneNumberAsEmailDomain = ""
            };
            var config = new MfaConfig { EnableMfa = true };
            var userInfo = new UserInfo 
            { 
                ItemId = "user-123",
                Email = "test@example.com",
                UserMfaType = UserMfaType.TOTP 
            };
            var expectedResponse = new OtpGenerationResponse { IsSuccess = true };
            var mockOtpService = new Mock<IOtpService>();

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(config);
            _mfaRepository.Setup(x => x.GetItemAsync<UserInfo>(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserInfo, bool>>>(), 
                "Users"))
                .ReturnsAsync(userInfo);
            _otpServiceFactory.Setup(x => x.GetOTPService(UserMfaType.TOTP))
                .Returns(mockOtpService.Object);
            mockOtpService.Setup(x => x.GenerateAsync(userInfo, ""))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _service.GenerateOTPAsync(request);

            // Assert
            result.Should().NotBeNull();
            _otpServiceFactory.Verify(x => x.GetOTPService(UserMfaType.TOTP), Times.Once);
        }

        #endregion

        #region VerifyOTPAsync

        [Fact]
        public async Task VerifyOTPAsync_WhenValid_AndNotFromTokenCall_UpdatesUserMfa()
        {
            // Arrange
            var request = new VerifyOtpRequest 
            { 
                VerificationCode = "123456",
                MfaId = "mfa-123",
                AuthType = UserMfaType.Email,
                IsFromTokenCall = false
            };
            var verificationResponse = new OtpVerificationResponse 
            { 
                IsValid = true, 
                UserId = "user-123" 
            };
            var mockOtpService = new Mock<IOtpService>();

            _otpServiceFactory.Setup(x => x.GetOTPService(UserMfaType.Email))
                .Returns(mockOtpService.Object);
            mockOtpService.Setup(x => x.VerifyAsync(request))
                .ReturnsAsync(verificationResponse);
            _mfaRepository.Setup(x => x.UpdatePartialAsync<UserMfaInfo>(
                "user-123",
                It.IsAny<Dictionary<string, object>>(),
                "Users"))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.VerifyOTPAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            result.UserId.Should().Be("user-123");
            _mfaRepository.Verify(x => x.UpdatePartialAsync<UserMfaInfo>(
                "user-123",
                It.Is<Dictionary<string, object>>(d => 
                    d.ContainsKey("MfaEnabled") && (bool)d["MfaEnabled"] == true &&
                    d.ContainsKey("IsMfaVerified") && (bool)d["IsMfaVerified"] == true),
                "Users"), Times.Once);
        }

        [Fact]
        public async Task VerifyOTPAsync_WhenValid_AndFromTokenCall_DoesNotUpdateUserMfa()
        {
            // Arrange
            var request = new VerifyOtpRequest 
            { 
                VerificationCode = "123456",
                MfaId = "mfa-123",
                AuthType = UserMfaType.TOTP,
                IsFromTokenCall = true
            };
            var verificationResponse = new OtpVerificationResponse 
            { 
                IsValid = true, 
                UserId = "user-123" 
            };
            var mockOtpService = new Mock<IOtpService>();

            _otpServiceFactory.Setup(x => x.GetOTPService(UserMfaType.TOTP))
                .Returns(mockOtpService.Object);
            mockOtpService.Setup(x => x.VerifyAsync(request))
                .ReturnsAsync(verificationResponse);

            // Act
            var result = await _service.VerifyOTPAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeTrue();
            _mfaRepository.Verify(x => x.UpdatePartialAsync<UserMfaInfo>(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task VerifyOTPAsync_WhenInvalid_DoesNotUpdateUserMfa()
        {
            // Arrange
            var request = new VerifyOtpRequest 
            { 
                VerificationCode = "wrong-code",
                MfaId = "mfa-123",
                AuthType = UserMfaType.Email,
                IsFromTokenCall = false
            };
            var verificationResponse = new OtpVerificationResponse 
            { 
                IsValid = false, 
                UserId = "user-123" 
            };
            var mockOtpService = new Mock<IOtpService>();

            _otpServiceFactory.Setup(x => x.GetOTPService(UserMfaType.Email))
                .Returns(mockOtpService.Object);
            mockOtpService.Setup(x => x.VerifyAsync(request))
                .ReturnsAsync(verificationResponse);

            // Act
            var result = await _service.VerifyOTPAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            _mfaRepository.Verify(x => x.UpdatePartialAsync<UserMfaInfo>(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region DisableUserMfa

        [Fact]
        public async Task DisableUserMfa_WhenUserIdIsEmpty_ReturnsError()
        {
            // Arrange
            var request = new DisableUserMfaRequest { UserId = "" };

            // Act
            var result = await _service.DisableUserMfa(request);

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().ContainKey("empty_user_id");
            result.Errors["empty_user_id"].Should().Be("User id should not be empty");
        }

        [Fact]
        public async Task DisableUserMfa_WhenUserIdDoesNotMatchContext_ReturnsError()
        {
            // Arrange
            var request = new DisableUserMfaRequest { UserId = "user-123" };
            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: "different-user",
                isAuthenticated: true,
                requestUri: "",
                organizationId: "",
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@example.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: "",
                displayName: "Test User",
                oauthToken: "",
                refreshToken: "",
                actualTentId: "test-tenant"
            );
            BlocksContext.SetContext(blocksContext);

            // Act
            var result = await _service.DisableUserMfa(request);

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().ContainKey("invalid_user_id");
            result.Errors["invalid_user_id"].Should().Be("Yor are not allowed to disable mfa");

            // Cleanup
            BlocksContext.SetContext(null);
        }

        [Fact]
        public async Task DisableUserMfa_WithValidRequest_DisablesMfa()
        {
            // Arrange
            var userId = "user-123";
            var request = new DisableUserMfaRequest { UserId = userId };
            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: userId,
                isAuthenticated: true,
                requestUri: "",
                organizationId: "",
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@example.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: "",
                displayName: "Test User",
                oauthToken: "",
                refreshToken: "",
                actualTentId: "test-tenant"
            );
            BlocksContext.SetContext(blocksContext);

            _mfaRepository.Setup(x => x.UpdatePartialAsync<UserMfaInfo>(
                userId,
                It.IsAny<Dictionary<string, object>>(),
                "Users"))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.DisableUserMfa(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            _mfaRepository.Verify(x => x.UpdatePartialAsync<UserMfaInfo>(
                userId,
                It.Is<Dictionary<string, object>>(d => 
                    d.ContainsKey("MfaEnabled") && (bool)d["MfaEnabled"] == false &&
                    d.ContainsKey("UserMfaType") && (UserMfaType)d["UserMfaType"] == UserMfaType.None &&
                    d.ContainsKey("IsMfaVerified") && (bool)d["IsMfaVerified"] == false),
                "Users"), Times.Once);

            // Cleanup
            BlocksContext.SetContext(null);
        }

        [Fact]
        public async Task DisableUserMfa_WhenContextIsNull_ReturnsError()
        {
            // Arrange
            var request = new DisableUserMfaRequest { UserId = "user-123" };
            BlocksContext.SetContext(null);

            // Act
            var result = await _service.DisableUserMfa(request);

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().ContainKey("invalid_user_id");
        }

        #endregion

        #region ResendOtpAsync

        [Fact]
        public async Task ResendOtpAsync_WhenMfaIdDoesNotExist_ReturnsError()
        {
            // Arrange
            var mfaId = "invalid-mfa-id";
            _cacheClient.Setup(x => x.KeyExistsAsync(mfaId)).ReturnsAsync(false);

            // Act
            var result = await _service.ResendOtpAsync(mfaId, "");

            // Assert
            result.Should().NotBeNull();
            result.Errors.Should().ContainKey("message");
            result.Errors["message"].Should().Be("invalid_two_factor_id");
        }

        [Fact]
        public async Task ResendOtpAsync_WithValidMfaId_GeneratesNewOTP()
        {
            // Arrange
            var mfaId = "mfa-123";
            var sendPhoneNumberAsEmailDomain = "example.com";
            var mfaContext = MfaAuthenticationContext.Create(mfaId, "user-123");
            var serializedContext = mfaContext.Sterilize();
            var config = new MfaConfig { EnableMfa = true };
            var userInfo = new UserInfo 
            { 
                ItemId = "user-123",
                Email = "test@example.com",
                UserMfaType = UserMfaType.Email 
            };
            var expectedResponse = new OtpGenerationResponse 
            { 
                IsSuccess = true, 
                MfaId = "new-mfa-123" 
            };
            var mockOtpService = new Mock<IOtpService>();

            _cacheClient.Setup(x => x.KeyExistsAsync(mfaId)).ReturnsAsync(true);
            _cacheClient.Setup(x => x.GetStringValueAsync(mfaId)).ReturnsAsync(serializedContext);
            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(config);
            _mfaRepository.Setup(x => x.GetItemAsync<UserInfo>(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserInfo, bool>>>(), 
                "Users"))
                .ReturnsAsync(userInfo);
            _otpServiceFactory.Setup(x => x.GetOTPService(UserMfaType.Email))
                .Returns(mockOtpService.Object);
            mockOtpService.Setup(x => x.GenerateAsync(userInfo, sendPhoneNumberAsEmailDomain))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _service.ResendOtpAsync(mfaId, sendPhoneNumberAsEmailDomain);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().Be("new-mfa-123");
            _cacheClient.Verify(x => x.KeyExistsAsync(mfaId), Times.Once);
            _cacheClient.Verify(x => x.GetStringValueAsync(mfaId), Times.Once);
        }

        [Fact]
        public async Task ResendOtpAsync_WithEmptyDomain_GeneratesOTP()
        {
            // Arrange
            var mfaId = "mfa-123";
            var mfaContext = MfaAuthenticationContext.Create(mfaId, "user-123");
            var serializedContext = mfaContext.Sterilize();
            var config = new MfaConfig { EnableMfa = true };
            var userInfo = new UserInfo
            { 
                ItemId = "user-123", 
                Email = "test@example.com",
                UserMfaType = UserMfaType.Email 
            };
            var expectedResponse = new OtpGenerationResponse { IsSuccess = true };
            var mockOtpService = new Mock<IOtpService>();

            _cacheClient.Setup(x => x.KeyExistsAsync(mfaId)).ReturnsAsync(true);
            _cacheClient.Setup(x => x.GetStringValueAsync(mfaId)).ReturnsAsync(serializedContext);
            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(config);
            _mfaRepository.Setup(x => x.GetItemAsync<UserInfo>(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserInfo, bool>>>(), 
                "Users"))
                .ReturnsAsync(userInfo);
            _otpServiceFactory.Setup(x => x.GetOTPService(UserMfaType.Email))
                .Returns(mockOtpService.Object);
            mockOtpService.Setup(x => x.GenerateAsync(userInfo, ""))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _service.ResendOtpAsync(mfaId, "");

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
        }

        #endregion
    }
}
