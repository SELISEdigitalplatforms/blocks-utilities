using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.OAuth.Services;
using DomainService.Services;
using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Moq;

namespace XUnitTest.DomainService.OAuth.Services
{
    public class MfaAuthorizationServiceTests
    {
        private readonly Mock<IOAuthJwtAccessTokenManager> _oAuthJwtAccessTokenManager = new();
        private readonly Mock<IOtpServiceFactory> _otpServiceFactory = new();
        private readonly Mock<IAuthenticationRepository> _oAuthRepository = new();
        private readonly MfaAuthorizationService _service;

        public MfaAuthorizationServiceTests()
        {
            _service = new MfaAuthorizationService(
                _oAuthJwtAccessTokenManager.Object,
                _otpServiceFactory.Object,
                _oAuthRepository.Object);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidMfaCode_ReturnsInvalidMfaCodeError()
        {
            // Arrange
            var request = new TokenRequest
            {
                MfaType = UserMfaType.TOTP,
                MfaId = "mfa-123",
                Code = "invalid-code",
                GrantType = "mfa"
            };
            var authConfig = new AuthenticationConfiguration();
            var mockOtpService = new Mock<IOtpService>();
            var verifyResponse = new OtpVerificationResponse
            {
                IsValid = false,
                UserId = "test-userId"
            };

            _otpServiceFactory
                .Setup(x => x.GetOTPService(request.MfaType))
                .Returns(mockOtpService.Object);
            mockOtpService
                .Setup(x => x.VerifyAsync(It.Is<VerifyOtpRequest>(r =>
                    r.AuthType == request.MfaType &&
                    r.MfaId == request.MfaId &&
                    r.VerificationCode == request.Code)))
                .ReturnsAsync(verifyResponse);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().Be("invalid_mfa_code");
            result.ErrorDescription.Should().Be("Mfa code is not valid");
            result.StatusCode.Should().Be(401);
            _otpServiceFactory.Verify(x => x.GetOTPService(request.MfaType), Times.Once);
            mockOtpService.Verify(
                x => x.VerifyAsync(It.IsAny<VerifyOtpRequest>()),
                Times.Once);
            _oAuthRepository.Verify(x => x.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<AuthenticationConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithValidMfaCodeAndVerifiedUser_ReturnsSuccessfulTokenResponse()
        {
            // Arrange
            var request = new TokenRequest
            {
                MfaType = UserMfaType.TOTP,
                MfaId = "mfa-123",
                Code = "123456",
                GrantType = "mfa"
            };
            var authConfig = new AuthenticationConfiguration();
            var mockOtpService = new Mock<IOtpService>();
            var verifyResponse = new OtpVerificationResponse
            {
                IsValid = true,
                UserId = "user-789"
            };
            var user = new User
            {
                ItemId = "user-789",
                Email = "test@example.com",
                Active = true,
                IsVarified = true,
                IsMfaVerified = true
            };
            var expectedTokenResponse = new TokenResponse
            {
                AccessToken = "access-token-123",
                ExpiresIn = 3600,
                RefreshToken = "refresh-token-456"
            };

            _otpServiceFactory
                .Setup(x => x.GetOTPService(request.MfaType))
                .Returns(mockOtpService.Object);
            mockOtpService
                .Setup(x => x.VerifyAsync(It.Is<VerifyOtpRequest>(r =>
                    r.AuthType == request.MfaType &&
                    r.MfaId == request.MfaId &&
                    r.VerificationCode == request.Code)))
                .ReturnsAsync(verifyResponse);
            _oAuthRepository
                .Setup(x => x.GetUserByIdAsync(verifyResponse.UserId))
                .ReturnsAsync(user);
            _oAuthJwtAccessTokenManager
                .Setup(x => x.ManageTokenAsync(request, authConfig, user, It.IsAny<StateInfo?>()))
                .ReturnsAsync(expectedTokenResponse);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.AccessToken.Should().Be("access-token-123");
            result.ExpiresIn.Should().Be(3600);
            result.RefreshToken.Should().Be("refresh-token-456");
            result.Error.Should().BeNullOrEmpty();
            _otpServiceFactory.Verify(x => x.GetOTPService(request.MfaType), Times.Once);
            mockOtpService.Verify(x => x.VerifyAsync(It.IsAny<VerifyOtpRequest>()), Times.Once);
            _oAuthRepository.Verify(x => x.GetUserByIdAsync(verifyResponse.UserId), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(x => x.ManageTokenAsync(request, authConfig, user, It.IsAny<StateInfo?>()), Times.Once);
        }
    }
}