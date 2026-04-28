using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.OAuth.Services;
using DomainService.Services;
using FluentAssertions;
using Iam.DomainService.Entities;
using Moq;
using System.Text.Json;

namespace XUnitTest.DomainService.OAuth.Services
{
    public class AuthorizeCodeServiceTests
    {
        private readonly Mock<IOAuthJwtAccessTokenManager> _oAuthJwtAccessTokenManager = new();
        private readonly Mock<IAuthenticationRepository> _oAuthRepository = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly AuthorizeCodeService _service;

        public AuthorizeCodeServiceTests()
        {
            _service = new AuthorizeCodeService(
                _oAuthJwtAccessTokenManager.Object,
                _oAuthRepository.Object,
                _cacheClient.Object);
        }

        [Fact]
        public async Task AuthenticateAsync_WithNullCacheData_ReturnsInvalidCodeError()
        {
            // Arrange
            var request = new TokenRequest { Code = "invalid-code" };
            var authConfig = new AuthenticationConfiguration();

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Code))
                .ReturnsAsync((string)null);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().Be("invalid_code");
            result.ErrorDescription.Should().Be("The code is either not valid or expire");
            _cacheClient.Verify(x => x.GetStringValueAsync(request.Code), Times.Once);
            _oAuthRepository.Verify(x => x.GetUserByEmailAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithEmptyUserNameInStateInfo_ReturnsInvalidCodeError()
        {
            // Arrange
            var request = new TokenRequest { Code = "test-code" };
            var authConfig = new AuthenticationConfiguration();
            var stateInfo = new StateInfo { UserName = "", Provider = "test-provider", Audience = "test-audience" };
            var serializedState = JsonSerializer.Serialize(stateInfo);

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Code))
                .ReturnsAsync(serializedState);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().Be("invalid_code");
            result.ErrorDescription.Should().Be("The code is either not valid or expire");
            _cacheClient.Verify(x => x.GetStringValueAsync(request.Code), Times.Once);
            _oAuthRepository.Verify(x => x.GetUserByEmailAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithNullUser_ReturnsInvalidResponse()
        {
            // Arrange
            var request = new TokenRequest { Code = "test-code", GrantType = "authorization_code" };
            var authConfig = new AuthenticationConfiguration();
            var stateInfo = new StateInfo { UserName = "test@example.com", Scope = "openid", Provider = "test-provider", Audience = "test-audience" };
            var serializedState = JsonSerializer.Serialize(stateInfo);

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Code))
                .ReturnsAsync(serializedState);
            _cacheClient.Setup(x => x.RemoveKeyAsync(request.Code))
                .ReturnsAsync(true);
            _oAuthRepository.Setup(x => x.GetUserByEmailAsync(stateInfo.UserName))
                .ReturnsAsync((User)null);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().NotBeNullOrEmpty();
            _cacheClient.Verify(x => x.RemoveKeyAsync(request.Code), Times.Once);
            _oAuthRepository.Verify(x => x.GetUserByEmailAsync(stateInfo.UserName), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<AuthenticationConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInactiveUser_ReturnsUserNotActiveOrVerifiedResponse()
        {
            // Arrange
            var request = new TokenRequest { Code = "test-code" };
            var authConfig = new AuthenticationConfiguration();
            var stateInfo = new StateInfo { UserName = "test@example.com", Scope = "openid", Provider = "test-provider", Audience = "test-audience" };
            var serializedState = JsonSerializer.Serialize(stateInfo);
            var user = new User { Email = "test@example.com", Active = false, IsVarified = true };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Code))
                .ReturnsAsync(serializedState);
            _cacheClient.Setup(x => x.RemoveKeyAsync(request.Code))
                .ReturnsAsync(true);
            _oAuthRepository.Setup(x => x.GetUserByEmailAsync(stateInfo.UserName))
                .ReturnsAsync(user);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().NotBeNullOrEmpty();
            _oAuthRepository.Verify(x => x.GetUserByEmailAsync(stateInfo.UserName), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<AuthenticationConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithUnverifiedUser_ReturnsUserNotActiveOrVerifiedResponse()
        {
            // Arrange
            var request = new TokenRequest { Code = "test-code" };
            var authConfig = new AuthenticationConfiguration();
            var stateInfo = new StateInfo { UserName = "test@example.com", Scope = "openid", Provider = "test-provider", Audience = "test-audience" };
            var serializedState = JsonSerializer.Serialize(stateInfo);
            var user = new User { Email = "test@example.com", Active = true, IsVarified = false };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Code))
                .ReturnsAsync(serializedState);
            _cacheClient.Setup(x => x.RemoveKeyAsync(request.Code))
                .ReturnsAsync(true);
            _oAuthRepository.Setup(x => x.GetUserByEmailAsync(stateInfo.UserName))
                .ReturnsAsync(user);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().NotBeNullOrEmpty();
            _oAuthRepository.Verify(x => x.GetUserByEmailAsync(stateInfo.UserName), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<AuthenticationConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithValidActiveVerifiedUser_ReturnsSuccessfulTokenResponse()
        {
            // Arrange
            var request = new TokenRequest { Code = "test-code", GrantType = "authorization_code" };
            var authConfig = new AuthenticationConfiguration();
            var stateInfo = new StateInfo { UserName = "test@example.com", Scope = "openid profile", Provider = "test-provider", Audience = "test-audience" };
            var serializedState = JsonSerializer.Serialize(stateInfo);
            var user = new User
            {
                Email = "test@example.com",
                Active = true,
                IsVarified = true,
                ItemId = "user123"
            };
            var expectedTokenResponse = new TokenResponse
            {
                AccessToken = "access-token-123",
                ExpiresIn = 3600,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(3600),
                RefreshToken = "refresh-token-456",
                RefreshExpiresUtc = DateTime.UtcNow.AddDays(30),
                Error = null,
                ErrorDescription = string.Empty,
                CookieDomain = ".example.com",
                MfaId = "mfa-123",
                UserMfa = UserMfaType.TOTP,
                StatusCode = 200
            };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Code))
                .ReturnsAsync(serializedState);
            _cacheClient.Setup(x => x.RemoveKeyAsync(request.Code))
                .ReturnsAsync(true);
            _oAuthRepository.Setup(x => x.GetUserByEmailAsync(stateInfo.UserName))
                .ReturnsAsync(user);
            _oAuthJwtAccessTokenManager.Setup(x => x.ManageTokenAsync(
                    It.IsAny<TokenRequest>(),
                    authConfig,
                    user,
                    stateInfo))
                .ReturnsAsync(expectedTokenResponse);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.AccessToken.Should().Be("access-token-123");
            result.ExpiresIn.Should().Be(3600);
            result.ExpiresUtc.Should().BeCloseTo(expectedTokenResponse.ExpiresUtc, TimeSpan.FromSeconds(1));
            result.RefreshToken.Should().Be("refresh-token-456");
            result.RefreshExpiresUtc.Should().BeCloseTo(expectedTokenResponse.RefreshExpiresUtc, TimeSpan.FromSeconds(1));
            result.Error.Should().BeNullOrEmpty();
            result.ErrorDescription.Should().BeEmpty();
            result.CookieDomain.Should().Be(".example.com");
            result.MfaId.Should().Be("mfa-123");
            result.UserMfa.Should().Be(UserMfaType.TOTP);
            result.StatusCode.Should().Be(200);
            request.Scope.Should().Be("openid profile");
            _cacheClient.Verify(x => x.GetStringValueAsync(request.Code), Times.Once);
            _cacheClient.Verify(x => x.RemoveKeyAsync(request.Code), Times.Once);
            _oAuthRepository.Verify(x => x.GetUserByEmailAsync(stateInfo.UserName), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(request, authConfig, user, stateInfo),
                Times.Once);
        }

        [Fact]
        public async Task AuthenticateAsync_ValidRequest_RemovesCachedCodeAfterRetrieval()
        {
            // Arrange
            var request = new TokenRequest { Code = "test-code" };
            var authConfig = new AuthenticationConfiguration();
            var stateInfo = new StateInfo { UserName = "test@example.com", Scope = "openid", Provider = "test-provider", Audience = "test-audience" };
            var serializedState = JsonSerializer.Serialize(stateInfo);
            var user = new User { Email = "test@example.com", Active = true, IsVarified = true };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Code))
                .ReturnsAsync(serializedState);
            _cacheClient.Setup(x => x.RemoveKeyAsync(request.Code))
                .ReturnsAsync(true);
            _oAuthRepository.Setup(x => x.GetUserByEmailAsync(stateInfo.UserName))
                .ReturnsAsync(user);
            _oAuthJwtAccessTokenManager.Setup(x => x.ManageTokenAsync(
                    It.IsAny<TokenRequest>(),
                    It.IsAny<AuthenticationConfiguration>(),
                    It.IsAny<User>(),
                    It.IsAny<StateInfo>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "token" });

            // Act
            await _service.AuthenticateAsync(request, authConfig);

            // Assert
            _cacheClient.Verify(x => x.RemoveKeyAsync(request.Code), Times.Once);
        }

        [Fact]
        public async Task AuthenticateAsync_ValidRequest_SetsScopeFromStateInfo()
        {
            // Arrange
            var request = new TokenRequest { Code = "test-code", Scope = "" };
            var authConfig = new AuthenticationConfiguration();
            var expectedScope = "openid profile email";
            var stateInfo = new StateInfo { UserName = "test@example.com", Scope = expectedScope, Provider = "test-provider", Audience = "test-audience" };
            var serializedState = JsonSerializer.Serialize(stateInfo);
            var user = new User { Email = "test@example.com", Active = true, IsVarified = true };

            _cacheClient.Setup(x => x.GetStringValueAsync(request.Code))
                .ReturnsAsync(serializedState);
            _cacheClient.Setup(x => x.RemoveKeyAsync(request.Code))
                .ReturnsAsync(true);
            _oAuthRepository.Setup(x => x.GetUserByEmailAsync(stateInfo.UserName))
                .ReturnsAsync(user);
            _oAuthJwtAccessTokenManager.Setup(x => x.ManageTokenAsync(
                    It.IsAny<TokenRequest>(),
                    It.IsAny<AuthenticationConfiguration>(),
                    It.IsAny<User>(),
                    It.IsAny<StateInfo>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "token" });

            // Act
            await _service.AuthenticateAsync(request, authConfig);

            // Assert
            request.Scope.Should().Be(expectedScope);
        }
    }
}