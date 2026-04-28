using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.OAuth.Services;
using DomainService.Services;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace XUnitTest.DomainService.OAuth.Services
{
    public class BYOSsoAuthorizationServiceTests
    {
        private readonly Mock<ILogger<BYOSsoAuthorizationService>> _logger = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _oAuthJwtAccessTokenManager = new();
        private readonly Mock<IAuthenticationRepository> _oAuthRepository = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<ISocialLogInServiceProvider> _socialLogInServiceProvider = new();
        private readonly Mock<IUserManagementMutationService> _userManagementMutationService = new();
        private readonly Mock<IConfiguration> _configuration = new();
        private readonly BYOSsoAuthorizationService _service;

        public BYOSsoAuthorizationServiceTests()
        {
            _service = new BYOSsoAuthorizationService(
                _logger.Object,
                _oAuthJwtAccessTokenManager.Object,
                _oAuthRepository.Object,
                _cacheClient.Object,
                _socialLogInServiceProvider.Object,
                _userManagementMutationService.Object,
                _configuration.Object);
        }

        [Fact]
        public async Task AuthenticateAsync_WithMissingCodeOrState_ReturnsValidationError()
        {
            // Arrange
            var request = new TokenRequest { Code = null, State = "valid-state" };
            var authConfig = new AuthenticationConfiguration();

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().Be("code_require");
            result.ErrorDescription.Should().Be("code_require");
            result.StatusCode.Should().Be(400);
            _cacheClient.Verify(x => x.GetStringValueAsync(It.IsAny<string>()), Times.Never);
        }

        

        [Fact]
        public async Task AuthenticateAsync_WithValidActiveUser_ReturnsSuccessfulTokenResponse()
        {
            // Arrange
            var request = new TokenRequest { Code = "valid-code", State = "valid-state", GrantType = "social" };
            var authConfig = new AuthenticationConfiguration();
            var stateInfo = new StateInfo { Provider = "google", Audience = "test-audience" };
            var stateCacheData = JsonSerializer.Serialize(stateInfo);
            var externalUser = new Mock<IExternalUserData>();
            externalUser.Setup(x => x.Email).Returns("test@example.com");
            externalUser.Setup(x => x.ExternalProviderUserId).Returns("ext-user-123");
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

            _cacheClient.Setup(x => x.GetStringValueAsync(request.State))
                .ReturnsAsync(stateCacheData);
            _cacheClient.Setup(x => x.RemoveKeyAsync(request.State))
                .ReturnsAsync(true);
            _socialLogInServiceProvider.Setup(x => x.HandleSocialLogin(It.IsAny<StateInfo>()))
                .ReturnsAsync(externalUser.Object);
            _oAuthRepository.Setup(x => x.GetUserByEmailAsync(externalUser.Object.Email))
                .ReturnsAsync(activeUser);
            _oAuthJwtAccessTokenManager.Setup(x => x.ManageTokenAsync(request, authConfig, activeUser, It.IsAny<StateInfo?>()))
                .ReturnsAsync(expectedTokenResponse);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.AccessToken.Should().Be("access-token-123");
            result.ExpiresIn.Should().Be(3600);
            result.RefreshToken.Should().Be("refresh-token-456");
            result.Error.Should().BeNullOrEmpty();
            _cacheClient.Verify(x => x.RemoveKeyAsync(request.State), Times.Once);
            _socialLogInServiceProvider.Verify(x => x.HandleSocialLogin(It.IsAny<StateInfo>()), Times.Once);
            _oAuthRepository.Verify(x => x.GetUserByEmailAsync(externalUser.Object.Email), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(x => x.ManageTokenAsync(request, authConfig, activeUser, It.IsAny<StateInfo?>()), Times.Once);
        }
    }
}