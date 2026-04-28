using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.OAuth.SocialServices
{
    public class GoogleLogInServiceTests
    {
        private readonly Mock<ILogger<GoogleLogInService>> _logger;
        private readonly Mock<IAuthenticationRepository> _authenticationRepository;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IHttpService> _httpService;
        private readonly GoogleLogInService _service;

        public GoogleLogInServiceTests()
        {
            _logger = new Mock<ILogger<GoogleLogInService>>();
            _authenticationRepository = new Mock<IAuthenticationRepository>();
            _cacheClient = new Mock<ICacheClient>();
            _httpService = new Mock<IHttpService>();
            _service = new GoogleLogInService(_logger.Object, _authenticationRepository.Object, _cacheClient.Object, _httpService.Object);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithValidCredential_ReturnsGoogleUri()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "google",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "google",
                Audience = "test-audience",
                AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth?scope={0}&state={1}&redirect_uri={2}&response_type=code&client_id={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                TokenUrl = "https://oauth2.googleapis.com/token",
                GetProfileUrl = "https://www.googleapis.com/oauth2/v2/userinfo?access_token={0}",
                RedirectUrl = "https://example.com/redirect",
                Scope = "openid email profile",
                SendAsResponse = false
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
                Provider = "google",
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
        public async Task HandleSocialLogin_WithAccessTokenError_ReturnsEmptyGoogleUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "google",
                Audience = "test-audience",
                Code = "test-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "google",
                Audience = "test-audience",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/redirect",
                TokenUrl = "https://oauth2.googleapis.com/token",
                GetProfileUrl = "https://www.googleapis.com/oauth2/v2/userinfo?access_token={0}",
                AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth",
                Scope = "openid email profile"
            };

            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService.Setup(x => x.SendFormUrlEncoded<SocialOauthAccessToken>(
                HttpMethod.Post, It.IsAny<Dictionary<string, string>>(), credential.TokenUrl, null, default))
                .ReturnsAsync(((SocialOauthAccessToken?)null, "Invalid credentials"));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<GoogleUserData>(result);
            var userData = (GoogleUserData)result;
            Assert.Null(userData.ExternalProviderUserId);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error while getting access token")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}