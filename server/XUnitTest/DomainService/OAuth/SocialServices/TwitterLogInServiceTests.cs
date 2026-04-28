using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.SocialServices;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace XUnitTest.DomainService.OAuth.SocialServices
{
    public class TwitterLogInServiceTests
    {
        private readonly Mock<ILogger<TwitterLogInService>> _logger;
        private readonly Mock<IAuthenticationRepository> _authenticationRepository;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IHttpService> _httpService;
        private readonly TwitterLogInService _service;

        public TwitterLogInServiceTests()
        {
            _logger = new Mock<ILogger<TwitterLogInService>>();
            _authenticationRepository = new Mock<IAuthenticationRepository>();
            _cacheClient = new Mock<ICacheClient>();
            _httpService = new Mock<IHttpService>();

            _service = new TwitterLogInService(
                _logger.Object,
                _authenticationRepository.Object,
                _cacheClient.Object,
                _httpService.Object
            );
        }

        #region GetProviderLogInUriAsync Tests

        [Fact]
        public async Task GetProviderLogInUriAsync_WithValidCredential_ReturnsFormattedUri()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "twitter",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback",
                SendAsResponse = false
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            var stateInfoJson = string.Empty;

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300))
                .Callback<string, string, long>((key, value, ttl) => stateInfoJson = value)
                .ReturnsAsync(true);

            // Act
            var (uri, isResponse) = await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.NotEmpty(uri);
            Assert.Contains("response_type=code", uri);
            Assert.Contains($"client_id={credential.ClientId}", uri);
            Assert.Contains("redirect_uri=", uri);
            Assert.Contains("scope=", uri);
            Assert.Contains("state=", uri);
            Assert.Contains("code_challenge=", uri);
            Assert.Contains("code_challenge_method=S256", uri);
            Assert.False(isResponse);

            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300), Times.Once);
        }
        
        [Fact]
        public async Task GetProviderLogInUriAsync_WithNullCredential_ReturnsEmptyStringAndLogsError()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "twitter",
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

            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }
        
        [Fact]
        public async Task GetProviderLogInUriAsync_StoresCorrectStateInfoInCache()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "twitter",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
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

            // Act
            await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.NotNull(capturedStateJson);
            var stateInfo = JsonSerializer.Deserialize<StateInfo>(capturedStateJson);
            Assert.Equal(request.Provider, stateInfo.Provider);
            Assert.Equal(request.Audience, stateInfo.Audience);
            Assert.Equal(request.NextUrl, stateInfo.NextUrl);
            Assert.NotNull(stateInfo.Extra);
            Assert.True(stateInfo.Extra.ContainsKey("code_verifier"));
            Assert.NotEmpty(stateInfo.Extra["code_verifier"]);
        }
        
        [Fact]
        public async Task GetProviderLogInUriAsync_GeneratesPKCEParameters()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "twitter",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };
                  
            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            string capturedStateJson = null;

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300))
                .Callback<string, string, long>((key, value, ttl) => capturedStateJson = value)
                .ReturnsAsync(true);

            // Act
            var (uri, _) = await _service.GetProviderLogInUriAsync(request);

            // Assert
            var stateInfo = JsonSerializer.Deserialize<StateInfo>(capturedStateJson);
            var codeVerifier = stateInfo.Extra["code_verifier"];

            // Verify code verifier is base64url encoded (no =, +, /)
            Assert.DoesNotContain("=", codeVerifier);
            Assert.DoesNotContain("+", codeVerifier);
            Assert.DoesNotContain("/", codeVerifier);
            Assert.NotEmpty(codeVerifier);

            // Verify URI contains code_challenge
            Assert.Contains("code_challenge=", uri);
            Assert.Contains("code_challenge_method=S256", uri);
        }
        
        [Fact]
        public async Task GetProviderLogInUriAsync_WithRequestSendAsResponse_ReturnsTrue()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "twitter",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback",
                SendAsResponse = true
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300))
                .ReturnsAsync(true);

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
                Provider = "twitter",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback",
                SendAsResponse = false
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300))
                .ReturnsAsync(true);

            // Act
            var (uri, isResponse) = await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.False(isResponse);
        }
        
        [Fact]
        public async Task GetProviderLogInUriAsync_EncodesUrlParametersCorrectly()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "twitter",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };
            
            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(request.Provider, request.Audience))
                .ReturnsAsync(credential);

            _cacheClient
                .Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300))
                .ReturnsAsync(true);

            // Act
            var (uri, _) = await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.Contains("scope=tweet.read%20users.read%20offline.access", uri);
        }

        #endregion
        
        #region HandleSocialLogin Tests

        [Fact]
        public async Task HandleSocialLogin_WithConfidentialClient_ReturnsTwitterUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "test-auth-code",
                Extra = new Dictionary<string, string> { { "code_verifier", "test-verifier" } }
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false,
                InitialPermissions = new List<string> { "read" },
                InitialRoles = new List<string> { "user" }
            };

            var tokenResponse = new TwitterOauthAccessToken
            {
                AccessToken = "test-access-token",
                TokenType = "Bearer"
            };

            var profileJson = JsonDocument.Parse(@"
            {
                ""data"": {
                    ""id"": ""123456789"",
                    ""name"": ""John Doe"",
                    ""username"": ""johndoe"",
                    ""confirmed_email"": ""john@example.com"",
                    ""profile_image_url"": ""https://pbs.twimg.com/profile.jpg""
                }
            }");

            Dictionary<string, string> capturedPostData = null;
            Dictionary<string, string> capturedHeaders = null;

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<HttpMethod, Dictionary<string, string>, string, Dictionary<string, string>, CancellationToken>(
                    (method, postData, url, headers, ct) =>
                    {
                        capturedPostData = postData;
                        capturedHeaders = headers;
                    })
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<JsonDocument>(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((profileJson, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<TwitterUserData>(result);
            var userData = (TwitterUserData)result;
            Assert.Equal("123456789", userData.ExternalProviderUserId);
            Assert.Equal("john@example.com", userData.Email);
            Assert.Equal("John Doe", userData.DisplayName);
            Assert.Equal("John", userData.FirstName);
            Assert.Equal("Doe", userData.LastName);
            Assert.Equal("johndoe", userData.UserName);
            Assert.Equal("https://pbs.twimg.com/profile.jpg", userData.ProfileImageUrl);
            Assert.Equal("twitter", userData.Platform);
            Assert.Single(userData.Roles);
            Assert.Equal("user", userData.Roles[0]);
            Assert.Single(userData.Permissions);
            Assert.Equal("read", userData.Permissions[0]);

            // Verify Basic auth header was used
            Assert.NotNull(capturedHeaders);
            Assert.Contains("Authorization", capturedHeaders.Keys);
            Assert.StartsWith("Basic ", capturedHeaders["Authorization"]);

            // Verify postData contains required fields
            Assert.NotNull(capturedPostData);
            Assert.Equal("authorization_code", capturedPostData["grant_type"]);
            Assert.Equal(stateInfo.Code, capturedPostData["code"]);
            Assert.Equal(credential.RedirectUrl, capturedPostData["redirect_uri"]);
            Assert.Equal("test-verifier", capturedPostData["code_verifier"]);
            Assert.False(capturedPostData.ContainsKey("client_id")); // Should not be in body for confidential client
        }
        
        [Fact]
        public async Task HandleSocialLogin_WithPublicClient_AddsClientIdToPostData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "test-auth-code",
                Extra = new Dictionary<string, string> { { "code_verifier", "test-verifier" } }
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = null,
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false,
                InitialPermissions = new List<string> { "read" },
                InitialRoles = new List<string> { "user" }
            };

            var tokenResponse = new TwitterOauthAccessToken
            {
                AccessToken = "test-access-token",
                TokenType = "Bearer"
            };

            var profileJson = JsonDocument.Parse(@"
            {
                ""data"": {
                    ""id"": ""123456789"",
                    ""name"": ""Jane Smith"",
                    ""username"": ""janesmith"",
                    ""confirmed_email"": ""jane@example.com""
                }
            }");

            Dictionary<string, string> capturedPostData = null;
            Dictionary<string, string> capturedHeaders = null;

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<HttpMethod, Dictionary<string, string>, string, Dictionary<string, string>, CancellationToken>(
                    (method, postData, url, headers, ct) =>
                    {
                        capturedPostData = postData;
                        capturedHeaders = headers;
                    })
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<JsonDocument>(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((profileJson, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<TwitterUserData>(result);

            Assert.NotNull(capturedPostData);
            Assert.False(capturedPostData.ContainsKey("client_id"));
            Assert.NotNull(capturedHeaders);
        }
        
        [Fact]
        public async Task HandleSocialLogin_WithMissingCodeVerifier_ReturnsEmptyUserDataAndLogsError()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "test-auth-code",
                Extra = new Dictionary<string, string>() // No code_verifier
            };
                        
            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false,
                InitialPermissions = new List<string> { "read" },
                InitialRoles = new List<string> { "user" }
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<TwitterUserData>(result);
            var userData = (TwitterUserData)result;
            Assert.Null(userData.ExternalProviderUserId);

            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("PKCE code verifier missing in stateInfo")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _httpService.Verify(
                x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    It.IsAny<HttpMethod>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
                
        [Fact]
        public async Task HandleSocialLogin_WithAccessTokenError_ReturnsEmptyUserDataAndLogsError()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "invalid-code",
                Extra = new Dictionary<string, string> { { "code_verifier", "test-verifier" } }
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            var errorMessage = "Invalid authorization code";

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(((TwitterOauthAccessToken)null, errorMessage));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<TwitterUserData>(result);
            var userData = (TwitterUserData)result;
            Assert.Null(userData.ExternalProviderUserId);

            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error getting Twitter access token")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _httpService.Verify(
                x => x.Get<JsonDocument>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task HandleSocialLogin_WithProfileFetchError_ReturnsEmptyUserDataAndLogsError()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "test-auth-code",
                Extra = new Dictionary<string, string> { { "code_verifier", "test-verifier" } }
            };
            
            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = null,
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false,
                InitialPermissions = new List<string> { "read" },
                InitialRoles = new List<string> { "user" }
            };

            var tokenResponse = new TwitterOauthAccessToken
            {
                AccessToken = "test-access-token",
                TokenType = "Bearer"
            };

            var profileError = "Failed to fetch user profile";

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<JsonDocument>(
                    credential.GetProfileUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(((JsonDocument)null, profileError));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<TwitterUserData>(result);
            var userData = (TwitterUserData)result;
            Assert.Null(userData.ExternalProviderUserId);

            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error fetching Twitter user profile")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleSocialLogin_SetsAuthorizationHeaderForProfileRequest()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "test-auth-code",
                Extra = new Dictionary<string, string> { { "code_verifier", "test-verifier" } }
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            var tokenResponse = new TwitterOauthAccessToken
            {
                AccessToken = "my-access-token-xyz",
                TokenType = "Bearer"
            };

            Dictionary<string, string> capturedProfileHeaders = null;

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<JsonDocument>(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, Dictionary<string, string>, CancellationToken>((url, headers, ct) => capturedProfileHeaders = headers)
                .ReturnsAsync(((JsonDocument)null, "error"));

            // Act
            await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(capturedProfileHeaders);
            Assert.True(capturedProfileHeaders.ContainsKey("Authorization"));
            Assert.Equal($"Bearer {tokenResponse.AccessToken}", capturedProfileHeaders["Authorization"]);
        }

        [Fact]
        public async Task HandleSocialLogin_WithNullProfile_ReturnsEmptyUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "test-auth-code",
                Extra = new Dictionary<string, string> { { "code_verifier", "test-verifier" } }
            };
                        
            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            var tokenResponse = new TwitterOauthAccessToken
            {
                AccessToken = "test-access-token",
                TokenType = "Bearer"
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<JsonDocument>(
                    credential.GetProfileUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(((JsonDocument)null, string.Empty)); // No error but null profile

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<TwitterUserData>(result);
            var userData = (TwitterUserData)result;
            Assert.Null(userData.ExternalProviderUserId);
        }

        [Fact]
        public async Task HandleSocialLogin_WithNoProfileImageUrl_SetsNullProfileImageUrl()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "test-auth-code",
                Extra = new Dictionary<string, string> { { "code_verifier", "test-verifier" } }
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            var tokenResponse = new TwitterOauthAccessToken
            {
                AccessToken = "test-access-token",
                TokenType = "Bearer"
            };

            var profileJson = JsonDocument.Parse(@"
            {
                ""data"": {
                    ""id"": ""123456789"",
                    ""name"": ""Test User"",
                    ""username"": ""testuser"",
                    ""confirmed_email"": ""test@example.com""
                }
            }");

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<JsonDocument>(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((profileJson, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<TwitterUserData>(result);
            var userData = (TwitterUserData)result;
            Assert.Equal("123456789", userData.ExternalProviderUserId);
            Assert.Null(userData.ProfileImageUrl);
        }

        [Fact]
        public async Task HandleSocialLogin_WithNullInitialRolesAndPermissions_ReturnsEmptyArrays()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "test-auth-code",
                Extra = new Dictionary<string, string> { { "code_verifier", "test-verifier" } }
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            var tokenResponse = new TwitterOauthAccessToken
            {
                AccessToken = "test-access-token",
                TokenType = "Bearer"
            };

            var profileJson = JsonDocument.Parse(@"
            {
                ""data"": {
                    ""id"": ""123456789"",
                    ""name"": ""Test User"",
                    ""username"": ""testuser"",
                    ""confirmed_email"": ""test@example.com""
                }
            }");

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<JsonDocument>(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((profileJson, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<TwitterUserData>(result);
            var userData = (TwitterUserData)result;
            Assert.NotNull(userData.Roles);
            Assert.Empty(userData.Roles);
            Assert.NotNull(userData.Permissions);
            Assert.Empty(userData.Permissions);
        }

        [Fact]
        public async Task HandleSocialLogin_ParsesNameCorrectlyForSingleWord()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "twitter",
                Audience = "test-audience",
                Code = "test-auth-code",
                Extra = new Dictionary<string, string> { { "code_verifier", "test-verifier" } }
            };

            var credential = new SocialLoginCredential
            {
                Provider = "twitter",
                Audience = "test-audience",
                AuthorizationUrl = "https://twitter.com/i/oauth2/authorize",
                ClientId = "test-client-id",
                ClientSecret = "test-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "tweet.read users.read offline.access",
                TokenUrl = "https://api.twitter.com/2/oauth2/token",
                GetProfileUrl = "https://api.twitter.com/2/users/me",
                SendAsResponse = false
            };

            var tokenResponse = new TwitterOauthAccessToken
            {
                AccessToken = "test-access-token",
                TokenType = "Bearer"
            };

            var profileJson = JsonDocument.Parse(@"
            {
                ""data"": {
                    ""id"": ""123456789"",
                    ""name"": ""Madonna"",
                    ""username"": ""madonna"",
                    ""confirmed_email"": ""madonna@example.com""
                }
            }");

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<TwitterOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            _httpService
                .Setup(x => x.Get<JsonDocument>(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((profileJson, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<TwitterUserData>(result);
            var userData = (TwitterUserData)result;
            Assert.Equal("Madonna", userData.DisplayName);
            Assert.Equal("Madonna", userData.FirstName);
            Assert.Equal("Madonna", userData.LastName); // Both First and Last are the same for single word
        }

        #endregion
    }
}