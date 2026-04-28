using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.SocialServices;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.OAuth.SocialServices
{
    public class LinkedinLogInServiceTests
    {
        private readonly Mock<ILogger<LinkedinLogInService>> _logger;
        private readonly Mock<IAuthenticationRepository> _authenticationRepository;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IHttpService> _httpService;
        private readonly LinkedinLogInService _service;

        public LinkedinLogInServiceTests()
        {
            _logger = new Mock<ILogger<LinkedinLogInService>>();
            _authenticationRepository = new Mock<IAuthenticationRepository>();
            _cacheClient = new Mock<ICacheClient>();
            _httpService = new Mock<IHttpService>();
            _service = new LinkedinLogInService(_logger.Object, _authenticationRepository.Object, _cacheClient.Object, _httpService.Object);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithValidCredential_ReturnsLinkedInUri()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "linkedin",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "linkedin",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.linkedin.com/oauth/v2/authorization",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/redirect",
                TokenUrl = "https://www.linkedin.com/oauth/v2/accessToken",
                GetProfileUrl = "https://www.linkedin.com/oauth/v2/userinfo?",
                Scope = "openid profile email",
                InitialPermissions = new List<string> { "read" },
                InitialRoles = new List<string> { "user" }
            };

            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Item2);
            Assert.Contains("response_type=code", result.Item1);
            Assert.Contains($"client_id={credential.ClientId}", result.Item1);
            Assert.Contains("scope=", result.Item1);
            Assert.Contains("state=", result.Item1);
            Assert.Contains("redirect_uri=", result.Item1);
            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300), Times.Once);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithNullCredential_ReturnsEmptyStringAndLogsError()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "linkedin",
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
        public async Task HandleSocialLogin_WithSuccessfulFlow_ReturnsLinkedInUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "linkedin",
                Audience = "test-audience",
                Code = "test-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "linkedin",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.linkedin.com/oauth/v2/authorization",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/redirect",
                TokenUrl = "https://www.linkedin.com/oauth/v2/accessToken",
                GetProfileUrl = "https://www.linkedin.com/oauth/v2/userinfo?",
                Scope = "openid profile email",
                InitialPermissions = new List<string> { "read" },
                InitialRoles = new List<string> { "user" }
            };

            var accessTokenResponse = new SocialOauthAccessToken
            {
                AccessToken = "linkedin-access-token",
                TokenType = "Bearer"
            };

            var linkedinUserInfo = new LinkedinUserInfo
            {
                Sub = "linkedin-user-123",
                Name = "Test User",
                Given_Name = "Test",
                Family_Name = "User",
                Email = "test@linkedin.com",
                Picture = "https://media.licdn.com/dms/image/test.jpg"
            };

            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService.Setup(x => x.SendFormUrlEncoded<SocialOauthAccessToken>(
                HttpMethod.Post,
                It.IsAny<Dictionary<string, string>>(),
                credential.TokenUrl,
                null,
                default))
                .ReturnsAsync((accessTokenResponse, string.Empty));

            _httpService.Setup(x => x.Get<LinkedinUserInfo>(
                It.IsAny<string>(),
                null,
                default))
                .ReturnsAsync((linkedinUserInfo, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<LinkedinUserData>(result);
            var userData = (LinkedinUserData)result;
            Assert.Equal("linkedin-user-123", userData.ExternalProviderUserId);
            Assert.Equal("test@linkedin.com", userData.Email);
            Assert.Equal("Test User", userData.DisplayName);
            Assert.Equal("Test", userData.FirstName);
            Assert.Equal("User", userData.LastName);
            Assert.Equal("linkedin", userData.Platform);
            Assert.Equal(credential.InitialPermissions, userData.Permissions);
            Assert.Equal(credential.InitialRoles, userData.Roles);
        }

        [Fact]
        public async Task HandleSocialLogin_WithAccessTokenError_ReturnsEmptyLinkedInUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "linkedin",
                Audience = "test-audience",
                Code = "test-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "linkedin",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.linkedin.com/oauth/v2/authorization",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/redirect",
                TokenUrl = "https://www.linkedin.com/oauth/v2/accessToken",
                GetProfileUrl = "https://www.linkedin.com/oauth/v2/userinfo?",
                Scope = "openid profile email",
                InitialPermissions = new List<string> { "read" },
                InitialRoles = new List<string> { "user" }
            };

            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService.Setup(x => x.SendFormUrlEncoded<SocialOauthAccessToken>(
                HttpMethod.Post,
                It.IsAny<Dictionary<string, string>>(),
                credential.TokenUrl,
                null,
                default))
                .ReturnsAsync(((SocialOauthAccessToken?)null, "Invalid credentials"));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<LinkedinUserData>(result);
            var userData = (LinkedinUserData)result;
            Assert.Null(userData.ExternalProviderUserId);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error while getting LinkedIn access token")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}