using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Entities;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;

namespace XUnitTest.DomainService.Mfa
{
    public class TotpServiceTests
    {
        private readonly Mock<IMfaManagementRepository> _repository;
        private readonly Mock<ILogger<TotpService>> _logger;
        private readonly Mock<IHttpContextAccessor> _httpContextAccessor;
        private readonly Mock<IConfiguration> _configuration;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IValidator<VerifyOtpRequest>> _validator;
        private readonly Mock<ITenants> _tenant;
        private readonly TotpService _service;

        public TotpServiceTests()
        {
            _repository = new Mock<IMfaManagementRepository>();
            _logger = new Mock<ILogger<TotpService>>();
            _httpContextAccessor = new Mock<IHttpContextAccessor>();
            _configuration = new Mock<IConfiguration>();
            _cacheClient = new Mock<ICacheClient>();
            _validator = new Mock<IValidator<VerifyOtpRequest>>();
            _tenant = new Mock<ITenants>();

            _service = new TotpService(
                _repository.Object,
                _logger.Object,
                _httpContextAccessor.Object,
                _configuration.Object,
                _cacheClient.Object,
                _validator.Object,
                _tenant.Object);
        }

        #region GenerateAsync Tests

        [Fact]
        public async Task GenerateAsync_WithValidUserInfo_GeneratesMfaIdAndCachesUserId()
        {
            // Arrange
            var userInfo = new UserInfo { ItemId = "user-123" };
            _cacheClient.Setup(x => x.AddStringValueAsync(
                It.IsAny<string>(),
                userInfo.ItemId,
                It.IsAny<long>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.GenerateAsync(userInfo);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().NotBeNullOrEmpty();
            _cacheClient.Verify(x => x.AddStringValueAsync(
                It.IsAny<string>(),
                userInfo.ItemId,
                15 * 60), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_WithNullDomain_IgnoresDomainParameter()
        {
            // Arrange
            var userInfo = new UserInfo { ItemId = "user-123" };
            _cacheClient.Setup(x => x.AddStringValueAsync(
                It.IsAny<string>(),
                userInfo.ItemId,
                It.IsAny<long>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.GenerateAsync(userInfo, null);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
        }

        #endregion

        #region GenerateTotpImageByUserAsync Tests

        [Fact]
        public async Task GenerateTotpImageByUserAsync_WhenUserNotFound_ReturnsUserNotExistError()
        {
            // Arrange
            var userId = "nonexistent-user";
            _repository.Setup(x => x.GetItemAsync<UserInfo>(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserInfo, bool>>>(),
                "Users"))
                .ReturnsAsync((UserInfo)null);

            // Act
            var result = await _service.GenerateTotpImageByUserAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("user_not_exist");
            result.Errors["user_not_exist"].Should().Contain(userId);
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_WhenExistingOtpWithImage_ReturnsExistingImage()
        {
            // Arrange
            var userId = "user-123";
            var userInfo = new UserInfo { ItemId = userId, Email = "test@example.com" };
            var existingOtp = new UserTotpDetail
            {
                ImageUri = "https://example.com/qr.png",
                Secret = "ABCDEFGH"
            };

            _repository.Setup(x => x.GetItemAsync<UserInfo>(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserInfo, bool>>>(),
                "Users"))
                .ReturnsAsync(userInfo);
            _repository.Setup(x => x.GetItemAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserTotpDetail, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync(existingOtp);

            // Act
            var result = await _service.GenerateTotpImageByUserAsync(userId);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.QrImageUrl.Should().Be(existingOtp.ImageUri);
            result.QrCode.Should().Be(existingOtp.Secret);
        }

        #endregion

        #region VerifyAsync Tests

        [Fact]
        public async Task VerifyAsync_WithInvalidRequest_ReturnsValidationErrors()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = "",
                MfaId = "mfa-123"
            };
            var validationErrors = new List<ValidationFailure>
            {
                new ValidationFailure("VerificationCode", "Verification code is required")
            };
            var validationResult = new ValidationResult(validationErrors);

            _validator.Setup(x => x.ValidateAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(validationResult);

            // Act
            var result = await _service.VerifyAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
        }

        [Fact]
        public async Task VerifyAsync_WhenMfaIdNotInCache_ReturnsSessionExpiredError()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = "123456",
                MfaId = "mfa-123",
                AuthType = UserMfaType.TOTP
            };

            _validator.Setup(x => x.ValidateAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _cacheClient.Setup(x => x.KeyExistsAsync(request.MfaId))
                .ReturnsAsync(false);

            // Act
            var result = await _service.VerifyAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("login_session_expired");
        }

        [Fact]
        public async Task VerifyAsync_WithValidTotpCode_ReturnsSuccessWithValidTrue()
        {
            // Arrange
            var userId = "user-123";
            var secret = "JBSWY3DPEHPK3PXP"; // Valid base32 secret
            var request = new VerifyOtpRequest
            {
                VerificationCode = GenerateValidTotp(secret),
                MfaId = "mfa-123",
                AuthType = UserMfaType.TOTP
            };
            var totpDetail = new UserTotpDetail
            {
                CreatedBy = userId,
                Secret = secret
            };

            _validator.Setup(x => x.ValidateAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _cacheClient.Setup(x => x.KeyExistsAsync(request.MfaId))
                .ReturnsAsync(true);
            _cacheClient.Setup(x => x.GetStringValueAsync(request.MfaId))
                .ReturnsAsync(userId);
            _repository.Setup(x => x.GetItemAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserTotpDetail, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync(totpDetail);

            // Act
            var result = await _service.VerifyAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.IsValid.Should().BeTrue();
            result.UserId.Should().Be(userId);
        }

        [Fact]
        public async Task VerifyAsync_WithInvalidTotpCode_ReturnsSuccessWithValidFalse()
        {
            // Arrange
            var userId = "user-123";
            var secret = "JBSWY3DPEHPK3PXP";
            var request = new VerifyOtpRequest
            {
                VerificationCode = "000000", // Invalid code
                MfaId = "mfa-123",
                AuthType = UserMfaType.TOTP
            };
            var totpDetail = new UserTotpDetail
            {
                CreatedBy = userId,
                Secret = secret
            };

            _validator.Setup(x => x.ValidateAsync(request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _cacheClient.Setup(x => x.KeyExistsAsync(request.MfaId))
                .ReturnsAsync(true);
            _cacheClient.Setup(x => x.GetStringValueAsync(request.MfaId))
                .ReturnsAsync(userId);
            _repository.Setup(x => x.GetItemAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserTotpDetail, bool>>>(),
                It.IsAny<string>()))
                .ReturnsAsync(totpDetail);

            // Act
            var result = await _service.VerifyAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.IsValid.Should().BeFalse();
            result.UserId.Should().Be(userId);
        }

        #endregion

        #region Helper Methods

        private BlocksContext CreateBlocksContext()
        {
            return BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: "test-user",
                isAuthenticated: true,
                requestUri: "",
                organizationId: "",
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@example.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: "",
                displayName: "Test User",
                oauthToken: "test-token",
                refreshToken: "",
                actualTentId: "test-tenant"
            );
        }

        private string GenerateValidTotp(string secret)
        {
            var key = OtpNet.Base32Encoding.ToBytes(secret);
            var totp = new OtpNet.Totp(key);
            return totp.ComputeTotp();
        }

        #endregion
    }
}
