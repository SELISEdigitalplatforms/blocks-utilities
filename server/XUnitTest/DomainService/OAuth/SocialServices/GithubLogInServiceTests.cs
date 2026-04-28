using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.OAuth.SocialServices
{
    public class GithubLogInServiceTests
    {
        private readonly Mock<ILogger<GithubLogInService>> _logger;
        private readonly Mock<IAuthenticationRepository> _authenticationRepository;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IHttpService> _httpService;
        private readonly GithubLogInService _service;

        public GithubLogInServiceTests()
        {
            _logger = new Mock<ILogger<GithubLogInService>>();
            _authenticationRepository = new Mock<IAuthenticationRepository>();
            _cacheClient = new Mock<ICacheClient>();
            _httpService = new Mock<IHttpService>();
            _service = new GithubLogInService(_logger.Object, _authenticationRepository.Object, _cacheClient.Object, _httpService.Object);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithValidCredential_ReturnsGithubUri()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "github",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "github",
                Audience = "test-audience",
                AuthorizationUrl = "https://github.com/login/oauth/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                TokenUrl = "https://github.com/login/oauth/access_token",
                GetProfileUrl = "https://api.github.com/user",
                RedirectUrl = "https://example.com/redirect",
                Scope = "user:email",
                SendAsResponse = false
            };

            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.True(result.Item2);
            Assert.Contains("response_type=code", result.Item1);
            Assert.Contains($"client_id={credential.ClientId}", result.Item1);
            Assert.Contains($"scope={credential.Scope}", result.Item1);
            Assert.Contains("state=", result.Item1);
            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300), Times.Once);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithNullCredential_ReturnsEmptyStringAndLogsError()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "github",
                Audience = "invalid-audience",
                NextUrl = "https://example.com/callback"
            };

            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync((SocialLoginCredential)null);

            // Act
            var result = await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.Equal(string.Empty, result.Item1);
            Assert.True(result.Item2);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"Credential not found for provider {request.Provider} and audience {request.Audience}")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleSocialLogin_WithSuccessfulFlow_ReturnsGithubUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "github",
                Audience = "test-audience",
                Code = "test-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "github",
                Audience = "test-audience",
                AuthorizationUrl = "https://github.com/login/oauth/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/redirect",
                TokenUrl = "https://github.com/login/oauth/access_token",
                GetProfileUrl = "https://api.github.com/user",
                Scope = "user:email",
                InitialPermissions = new List<string> { "read" },
                InitialRoles = new List<string> { "user" }
            };

            var accessTokenResponse = new SocialOauthAccessToken
            {
                AccessToken = "github-access-token",
                TokenType = "Bearer"
            };

            var githubUser = new GithubUserData
            {
                Id = 12345678,
                Login = "testuser",
                DisplayName = "Test User",
                Email = "test@github.com",
                ProfileImageUrl = "https://avatars.githubusercontent.com/u/12345678"
            };

            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService.Setup(x => x.SendFormUrlEncoded<SocialOauthAccessToken>(
                HttpMethod.Post,
                It.IsAny<Dictionary<string, string>>(),
                credential.TokenUrl,
                It.IsAny<Dictionary<string, string>>(),
                default))
                .ReturnsAsync((accessTokenResponse, string.Empty));

            _httpService.Setup(x => x.Get<GithubUserData>(
                credential.GetProfileUrl,
                It.IsAny<Dictionary<string, string>>(),
                default))
                .ReturnsAsync((githubUser, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<GithubUserData>(result);
            var userData = (GithubUserData)result;
            Assert.Equal("12345678", userData.ExternalProviderUserId);
            Assert.Equal("test@github.com", userData.Email);
            Assert.Equal("github", userData.Platform);
            Assert.Equal(credential.InitialPermissions, userData.Permissions);
            Assert.Equal(credential.InitialRoles, userData.Roles);
        }

        [Fact]
        public async Task HandleSocialLogin_WithAccessTokenError_ReturnsEmptyGithubUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "github",
                Audience = "test-audience",
                Code = "test-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "github",
                Audience = "test-audience",
                AuthorizationUrl = "https://github.com/login/oauth/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/redirect",
                TokenUrl = "https://github.com/login/oauth/access_token",
                GetProfileUrl = "https://api.github.com/user",
                Scope = "user:email"
            };

            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService.Setup(x => x.SendFormUrlEncoded<SocialOauthAccessToken>(
                HttpMethod.Post,
                It.IsAny<Dictionary<string, string>>(),
                credential.TokenUrl,
                It.IsAny<Dictionary<string, string>>(),
                default))
                .ReturnsAsync(((SocialOauthAccessToken?)null, "Invalid credentials"));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<GithubUserData>(result);
            var userData = (GithubUserData)result;
            Assert.Null(userData.ExternalProviderUserId);
            Assert.Equal(0, userData.Id);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error while getting GitHub access token")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}