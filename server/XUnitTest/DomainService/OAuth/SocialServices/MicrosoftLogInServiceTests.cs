using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.OAuth.SocialServices
{
    public class MicrosoftLogInServiceTests
    {
        private readonly Mock<ILogger<MicrosoftLogInService>> _logger;
        private readonly Mock<IAuthenticationRepository> _authenticationRepository;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IHttpService> _httpService;
        private readonly MicrosoftLogInService _service;

        public MicrosoftLogInServiceTests()
        {
            _logger = new Mock<ILogger<MicrosoftLogInService>>();
            _authenticationRepository = new Mock<IAuthenticationRepository>();
            _cacheClient = new Mock<ICacheClient>();
            _httpService = new Mock<IHttpService>();
            _service = new MicrosoftLogInService(_logger.Object, _authenticationRepository.Object, _cacheClient.Object, _httpService.Object);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithValidCredential_ReturnsMicrosoftUri()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "microsoft",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "microsoft",
                Audience = "test-audience",
                AuthorizationUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?scope={0}&state={1}&redirect_uri={2}&response_type=code&client_id={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                TokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                GetProfileUrl = "https://graph.microsoft.com/v1.0/me",
                RedirectUrl = "https://example.com/redirect",
                Scope = "openid email profile User.Read",
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
                Provider = "microsoft",
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
        public async Task HandleSocialLogin_WithAccessTokenError_ReturnsEmptyMicrosoftUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "microsoft",
                Audience = "test-audience",
                Code = "test-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "microsoft",
                Audience = "test-audience",
                AuthorizationUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?scope={0}&state={1}&redirect_uri={2}&response_type=code&client_id={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                TokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                GetProfileUrl = "https://graph.microsoft.com/v1.0/me",
                RedirectUrl = "https://example.com/redirect",
                Scope = "openid email profile User.Read",
                SendAsResponse = false
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
            Assert.IsType<MicrosoftUserData>(result);
            var userData = (MicrosoftUserData)result;
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