using Authentication.DomainService.OAuth.SocialServices;
using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace XUnitTest.DomainService.OAuth.SocialServices
{
    public class FaceBookLogInServiceTests
    {
        private readonly Mock<ILogger<FaceBookLogInService>> _logger;
        private readonly Mock<IAuthenticationRepository> _authenticationRepository;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IHttpService> _httpService;
        private readonly FaceBookLogInService _service;

        public FaceBookLogInServiceTests()
        {
            _logger = new Mock<ILogger<FaceBookLogInService>>();
            _authenticationRepository = new Mock<IAuthenticationRepository>();
            _cacheClient = new Mock<ICacheClient>();
            _httpService = new Mock<IHttpService>();
            
            _service = new FaceBookLogInService(
                _logger.Object,
                _authenticationRepository.Object,
                _cacheClient.Object,
                _httpService.Object
            );
        }

        #region GetProviderLogInUriAsync Tests

        [Fact]
        public async Task GetProviderLogInUriAsync_WithNullCredential_ReturnsEmptyStringAndLogsError()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "facebook",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync((SocialLoginCredential)null);

            // Act
            var (uri, isResponse) = await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.Empty(uri);
            Assert.True(isResponse);
            
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"Credential not found for provider {request.Provider} and audience {request.Audience}")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
            
            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithRequestSendAsResponse_ReturnsTrue()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "facebook",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback",
                SendAsResponse = true
            };

            var credential = new SocialLoginCredential
            {
                Provider = "facebook",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.facebook.com/v18.0/dialog/oauth?client_id={0}&redirect_uri={1}&scope={2}&state={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "email,public_profile",
                TokenUrl = "https://graph.facebook.com/v18.0/oauth/access_token",
                GetProfileUrl = "https://graph.facebook.com/me?fields=id,name,email",
                SendAsResponse = false
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300))
                .ReturnsAsync(true);

            _cacheClient
                .Setup(x => x.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync("cached-state");

            // Act
            var (uri, isResponse) = await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.True(isResponse);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithCredentialSendAsResponse_ReturnsTrue()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "facebook",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback",
                SendAsResponse = false
            };

            var credential = new SocialLoginCredential
            {
                Provider = "facebook",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.facebook.com/v18.0/dialog/oauth?client_id={0}&redirect_uri={1}&scope={2}&state={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "email,public_profile",
                TokenUrl = "https://graph.facebook.com/v18.0/oauth/access_token",
                GetProfileUrl = "https://graph.facebook.com/me?fields=id,name,email",
                SendAsResponse = false
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300))
                .ReturnsAsync(true);

            _cacheClient
                .Setup(x => x.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync("cached-state");

            // Act
            var (uri, isResponse) = await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.False(isResponse);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_StoresCorrectStateInfoInCache()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "facebook",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "facebook",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.facebook.com/v18.0/dialog/oauth?client_id={0}&redirect_uri={1}&scope={2}&state={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "email,public_profile",
                TokenUrl = "https://graph.facebook.com/v18.0/oauth/access_token",
                GetProfileUrl = "https://graph.facebook.com/me?fields=id,name,email",
                SendAsResponse = false
            };

            string capturedStateJson = null;
            string capturedStateKey = null;

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300))
                .Callback<string, string, long>((key, value, ttl) =>
                {
                    capturedStateKey = key;
                    capturedStateJson = value;
                })
                .ReturnsAsync(true);

            _cacheClient
                .Setup(x => x.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string key) => capturedStateJson);

            // Act
            await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.NotNull(capturedStateJson);
            var stateInfo = JsonSerializer.Deserialize<StateInfo>(capturedStateJson);
            Assert.Equal(request.Provider, stateInfo.Provider);
            Assert.Equal(request.Audience, stateInfo.Audience);
            Assert.Equal(request.NextUrl, stateInfo.NextUrl);
        }

        #endregion


        #region HandleSocialLogin Tests

        [Fact]
        public async Task HandleSocialLogin_WithSuccessfulFlow_ReturnsFacebookUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "facebook",
                Audience = "test-audience",
                Code = "test-auth-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "facebook",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.facebook.com/v18.0/dialog/oauth?client_id={0}&redirect_uri={1}&scope={2}&state={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                TokenUrl = "https://graph.facebook.com/v18.0/oauth/access_token",
                Scope = "email,public_profile",
                GetProfileUrl = "https://graph.facebook.com/me?fields=id,name,email",
                SendAsResponse = false,
                InitialPermissions = new List<string> { "read" },
                InitialRoles = new List<string> { "user" }
            };

            var tokenResponse = new SocialOauthAccessToken
            {
                AccessToken = "test-access-token",
                TokenType = "Bearer"
            };

            var facebookUserData = new FaceBookUserData
            {
                ExternalProviderUserId = "fb-123456789",
                Email = "test@facebook.com",
                DisplayName = "Test User",
                Platform = "facebook"
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.Get<SocialOauthAccessToken>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<FaceBookUserData>(
                    credential.GetProfileUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((facebookUserData, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<FaceBookUserData>(result);
            var userData = (FaceBookUserData)result;
            Assert.Equal("fb-123456789", userData.ExternalProviderUserId);
            Assert.Equal("test@facebook.com", userData.Email);
            Assert.Equal("facebook", userData.Platform);

            _logger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("faceBook Access Token Uri")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleSocialLogin_WithAccessTokenError_ReturnsEmptyUserDataAndLogsError()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "facebook",
                Audience = "test-audience",
                Code = "invalid-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "facebook",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.facebook.com/v18.0/dialog/oauth?client_id={0}&redirect_uri={1}&scope={2}&state={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "email,public_profile",
                TokenUrl = "https://graph.facebook.com/v18.0/oauth/access_token",
                GetProfileUrl = "https://graph.facebook.com/me?fields=id,name,email"
            };

            var errorMessage = "Invalid authorization code";

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.Get<SocialOauthAccessToken>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(((SocialOauthAccessToken)null, errorMessage));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<FaceBookUserData>(result);
            var userData = (FaceBookUserData)result;
            Assert.Null(userData.ExternalProviderUserId);

            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error getting facebook access token")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _httpService.Verify(
                x => x.Get<FaceBookUserData>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleSocialLogin_WithProfileFetchError_ReturnsEmptyUserDataAndLogsError()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "facebook",
                Audience = "test-audience",
                Code = "test-auth-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "facebook",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.facebook.com/v18.0/dialog/oauth?client_id={0}&redirect_uri={1}&scope={2}&state={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "email,public_profile",
                TokenUrl = "https://graph.facebook.com/v18.0/oauth/access_token",
                GetProfileUrl = "https://graph.facebook.com/me?fields=id,name,email"
            };

            var tokenResponse = new SocialOauthAccessToken
            {
                AccessToken = "test-access-token",
                TokenType = "Bearer"
            };

            var profileError = "Failed to fetch user profile";

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.Get<SocialOauthAccessToken>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<FaceBookUserData>(
                    credential.GetProfileUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(((FaceBookUserData)null, profileError));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<FaceBookUserData>(result);
            var userData = (FaceBookUserData)result;
            Assert.Null(userData.ExternalProviderUserId);

            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error fetching Facebook user profile")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleSocialLogin_ConstructsCorrectAccessTokenUri()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "facebook",
                Audience = "test-audience",
                Code = "test-code-123"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "facebook",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.facebook.com/v18.0/dialog/oauth?client_id={0}&redirect_uri={1}&scope={2}&state={3}",
                ClientId = "client-id-456",
                ClientSecret = "secret-789",
                RedirectUrl = "https://example.com/callback",
                Scope = "email,public_profile",
                TokenUrl = "https://graph.facebook.com/v18.0/oauth/access_token",
                GetProfileUrl = "https://graph.facebook.com/me"
            };

            string capturedTokenUri = null;

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.Get<SocialOauthAccessToken>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Dictionary<string, string>, CancellationToken>((uri, headers, token) => capturedTokenUri = uri)
                .ReturnsAsync(((SocialOauthAccessToken)null, "error"));

            // Act
            await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(capturedTokenUri);
            Assert.Contains($"client_id={credential.ClientId}", capturedTokenUri);
            Assert.Contains($"client_secret={credential.ClientSecret}", capturedTokenUri);
            Assert.Contains($"redirect_uri={credential.RedirectUrl}", capturedTokenUri);
            Assert.Contains($"code={stateInfo.Code}", capturedTokenUri);
            Assert.StartsWith(credential.TokenUrl, capturedTokenUri);
        }

        [Fact]
        public async Task HandleSocialLogin_SetsAuthorizationHeaderForProfileRequest()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "facebook",
                Audience = "test-audience",
                Code = "test-auth-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "facebook",
                Audience = "test-audience",
                AuthorizationUrl = "https://www.facebook.com/v18.0/dialog/oauth?client_id={0}&redirect_uri={1}&scope={2}&state={3}",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "email,public_profile",
                TokenUrl = "https://graph.facebook.com/v18.0/oauth/access_token",
                GetProfileUrl = "https://graph.facebook.com/me?fields=id,name,email"
            };

            var tokenResponse = new SocialOauthAccessToken
            {
                AccessToken = "my-access-token-xyz",
                TokenType = "Bearer"
            };

            Dictionary<string, string> capturedHeaders = null;

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.Get<SocialOauthAccessToken>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<FaceBookUserData>(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, Dictionary<string, string>, CancellationToken>((url, headers, token) => capturedHeaders = headers)
                .ReturnsAsync(((FaceBookUserData)null, "error"));

            // Act
            await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(capturedHeaders);
            Assert.True(capturedHeaders.ContainsKey("Authorization"));
            Assert.Equal($"Bearer {tokenResponse.AccessToken}", capturedHeaders["Authorization"]);
        }
        #endregion
    }
}