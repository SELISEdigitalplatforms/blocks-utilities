using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.OAuth.Services;
using DomainService.Services;
using FluentAssertions;
using Iam.DomainService.Entities;
using Moq;

namespace XUnitTest.DomainService.OAuth.Services
{
    public class BiometricAuthorizationServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authenticationRepository = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _oAuthJwtAccessTokenManager = new();
        private readonly BiometricAuthorizationService _service;

        public BiometricAuthorizationServiceTests()
        {
            _service = new BiometricAuthorizationService(
                _authenticationRepository.Object,
                _oAuthJwtAccessTokenManager.Object);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidBiometricCredentials_ReturnsInvalidClientError()
        {
            // Arrange
            var request = new TokenRequest
            {
                BiometricId = "invalid-id",
                BiometricKey = "invalid-key"
            };
            var authConfig = new AuthenticationConfiguration();

            _authenticationRepository
                .Setup(x => x.AuthenticateBiometricCredentialAsync(request.BiometricId, request.BiometricKey))
                .ReturnsAsync((BiometricCredential)null);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().Be("invalid_client");
            result.ErrorDescription.Should().Be("The biometricId or biometricKey is not valid");
            _authenticationRepository.Verify(x => x.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<AuthenticationConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithNullUser_ReturnsInvalidCredentialsError()
        {
            // Arrange
            var request = new TokenRequest
            {
                BiometricId = "valid-id",
                BiometricKey = "valid-key"
            };
            var authConfig = new AuthenticationConfiguration();
            var biometricClient = new BiometricCredential { UserId = "user-123" };

            _authenticationRepository
                .Setup(x => x.AuthenticateBiometricCredentialAsync(request.BiometricId, request.BiometricKey))
                .ReturnsAsync(biometricClient);
            _authenticationRepository
                .Setup(x => x.GetUserByIdAsync(biometricClient.UserId))
                .ReturnsAsync((User)null);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().BeEmpty();
            result.ErrorDescription.Should().Be("The biometricId or biometricKey is not valid");
            _authenticationRepository.Verify(x => x.GetUserByIdAsync(biometricClient.UserId), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<AuthenticationConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInactiveUser_ReturnsInvalidCredentialsError()
        {
            // Arrange
            var request = new TokenRequest
            {
                BiometricId = "valid-id",
                BiometricKey = "valid-key"
            };
            var authConfig = new AuthenticationConfiguration();
            var biometricClient = new BiometricCredential { UserId = "user-123" };
            var inactiveUser = new User
            {
                ItemId = "user-123",
                Email = "test@example.com",
                Active = false,
                IsVarified = true
            };

            _authenticationRepository
                .Setup(x => x.AuthenticateBiometricCredentialAsync(request.BiometricId, request.BiometricKey))
                .ReturnsAsync(biometricClient);
            _authenticationRepository
                .Setup(x => x.GetUserByIdAsync(biometricClient.UserId))
                .ReturnsAsync(inactiveUser);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().BeEmpty();
            result.ErrorDescription.Should().Be("The biometricId or biometricKey is not valid");
            _authenticationRepository.Verify(x => x.GetUserByIdAsync(biometricClient.UserId), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<AuthenticationConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithValidCredentialsAndActiveUser_ReturnsSuccessfulTokenResponse()
        {
            // Arrange
            var request = new TokenRequest
            {
                BiometricId = "valid-id",
                BiometricKey = "valid-key",
                GrantType = "biometric"
            };
            var authConfig = new AuthenticationConfiguration();
            var biometricClient = new BiometricCredential 
            { 
                UserId = "user-123",
                PhysicalAddress = "physical-address",
                IsActive = true,
                BiometricId = "biometric-id",
                BiometriKey = "biometric-key",
                BiometricType = BiometricType.Fingerprint,
                DeviceInformation = "device-info"
            };
            var activeUser = new User
            {
                ItemId = "user-123",
                Email = "test@example.com",
                Active = true,
                IsVarified = true
            };
            var expectedTokenResponse = new TokenResponse
            {
                AccessToken = "access-token-123",
                ExpiresIn = 3600,
                RefreshToken = "refresh-token-456"
            };

            _authenticationRepository
                .Setup(x => x.AuthenticateBiometricCredentialAsync(request.BiometricId, request.BiometricKey))
                .ReturnsAsync(biometricClient);
            _authenticationRepository
                .Setup(x => x.GetUserByIdAsync(biometricClient.UserId))
                .ReturnsAsync(activeUser);
            _oAuthJwtAccessTokenManager
                .Setup(x => x.ManageTokenAsync(request, authConfig, activeUser, It.IsAny<StateInfo?>()))
                .ReturnsAsync(expectedTokenResponse);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.AccessToken.Should().Be("access-token-123");
            result.ExpiresIn.Should().Be(3600);
            result.RefreshToken.Should().Be("refresh-token-456");
            result.Error.Should().BeNullOrEmpty();
            _authenticationRepository.Verify(
                x => x.AuthenticateBiometricCredentialAsync(request.BiometricId, request.BiometricKey),
                Times.Once);
            _authenticationRepository.Verify(x => x.GetUserByIdAsync(biometricClient.UserId), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(request, authConfig, activeUser, It.IsAny<StateInfo?>()),
                Times.Once);
        }
    }
}