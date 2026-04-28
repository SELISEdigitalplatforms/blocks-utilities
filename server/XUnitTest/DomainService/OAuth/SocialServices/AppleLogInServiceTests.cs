using Authentication.DomainService.OAuth.SocialServices;
using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace XUnitTest.DomainService.OAuth.SocialServices
{
    public class AppleLogInServiceTests
    {
        private readonly Mock<ILogger<AppleLogInService>> _logger;
        private readonly Mock<IAuthenticationRepository> _authenticationRepository;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IHttpService> _httpService;
        private readonly AppleLogInService _service;

        public AppleLogInServiceTests()
        {
            _logger = new Mock<ILogger<AppleLogInService>>();
            _authenticationRepository = new Mock<IAuthenticationRepository>();
            _cacheClient = new Mock<ICacheClient>();
            _httpService = new Mock<IHttpService>();

            _service = new AppleLogInService(
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
                Provider = "apple",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback",
                SendAsResponse = false
            };

            var credential = new SocialLoginCredential
            {
                Provider = "apple",
                Audience = "test-audience",
                AuthorizationUrl = "https://appleid.apple.com/auth/authorize?client_id={0}&scope={1}&redirect_uri={2}&state={3}&response_type=code&response_mode=form_post",
                ClientId = "com.example.app",
                ClientSecret = "test-client-secret",
                RedirectUrl = "https://example.com/oauth/callback",
                TokenUrl = "https://appleid.apple.com/auth/token",
                GetProfileUrl = "https://appleid.apple.com/auth/profile",
                Scope = "name email",
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
            Assert.NotEmpty(uri);
            Assert.Contains($"client_id={credential.ClientId}", uri);
            //Assert.Contains("scope=name%20email", uri);
            Assert.Contains("redirect_uri=", uri);
            Assert.Contains("state=", uri);
            Assert.Contains("response_type=code", uri);
            Assert.False(isResponse);

            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 300), Times.Once);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithNullCredential_ReturnsEmptyStringAndLogsError()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "apple",
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
                Provider = "apple",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "apple",
                Audience = "test-audience",
                AuthorizationUrl = "https://appleid.apple.com/auth/authorize?client_id={0}&scope={1}&redirect_uri={2}&state={3}",
                ClientId = "com.example.app",
                ClientSecret = "test-client-secret",
                TokenUrl = "https://appleid.apple.com/auth/token",
                GetProfileUrl = "https://appleid.apple.com/auth/profile",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "name email",
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
            await _service.GetProviderLogInUriAsync(request);

            // Assert
            Assert.NotNull(capturedStateJson);
            var stateInfo = JsonSerializer.Deserialize<StateInfo>(capturedStateJson);
            Assert.Equal(request.Provider, stateInfo.Provider);
            Assert.Equal(request.Audience, stateInfo.Audience);
            Assert.Equal(request.NextUrl, stateInfo.NextUrl);
        }

        [Fact]
        public async Task GetProviderLogInUriAsync_WithRequestSendAsResponse_ReturnsTrue()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "apple",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback",
                SendAsResponse = true
            };

            var credential = new SocialLoginCredential
            {
                Provider = "apple",
                Audience = "test-audience",
                AuthorizationUrl = "https://appleid.apple.com/auth/authorize?client_id={0}&scope={1}&redirect_uri={2}&state={3}",
                ClientId = "com.example.app",
                ClientSecret = "test-client-secret",
                TokenUrl = "https://appleid.apple.com/auth/token",
                GetProfileUrl = "https://appleid.apple.com/auth/profile",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "name email",
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
                Provider = "apple",
                Audience = "test-audience",
                NextUrl = "https://example.com/callback",
                SendAsResponse = false
            };

            var credential = new SocialLoginCredential
            {
                Provider = "apple",
                Audience = "test-audience",
                AuthorizationUrl = "https://appleid.apple.com/auth/authorize?client_id={0}&scope={1}&redirect_uri={2}&state={3}",
                ClientId = "com.example.app",
                ClientSecret = "test-client-secret",
                TokenUrl = "https://appleid.apple.com/auth/token",
                GetProfileUrl = "https://appleid.apple.com/auth/profile",
                RedirectUrl = "https://example.com/oauth/callback",
                Scope = "name email",
                SendAsResponse = true
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

        #endregion

        #region HandleSocialLogin Tests

        [Fact]
        public async Task HandleSocialLogin_WithValidToken_ReturnsAppleUserData()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "apple",
                Audience = "test-audience",
                Code = "test-auth-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "apple",
                Audience = "test-audience",
                ClientId = "com.example.app",
                ClientSecret = "test-client-secret",
                AuthorizationUrl = "https://appleid.apple.com/auth/authorize",
                GetProfileUrl = "https://appleid.apple.com/auth/profile",
                Scope = "name email",
                TeamId = "TEAM123",
                KeyId = "KEY123",
                PrivateKey = GenerateTestECDsaKey(),
                AppleAudience = "https://appleid.apple.com",
                RedirectUrl = "https://example.com/oauth/callback",
                TokenUrl = "https://appleid.apple.com/auth/token",
                InitialRoles = new List<string> { "user" }
            };

            var idToken = GenerateTestIdToken("user@example.com", "001234.abcd1234");

            var tokenResponse = new SocialOauthAccessToken
            {
                AccessToken = "test-access-token",
                IdToken = idToken
            };

            Dictionary<string, string> capturedPostData = null;

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<SocialOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<HttpMethod, Dictionary<string, string>, string, Dictionary<string, string>, CancellationToken>(
                    (method, postData, url, headers, ct) => capturedPostData = postData)
                .ReturnsAsync((tokenResponse, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<AppleUserData>(result);
            var userData = (AppleUserData)result;
            Assert.Equal("user@example.com", userData.Email);
            Assert.Equal("001234.abcd1234", userData.ExternalProviderUserId);
            Assert.Equal("apple", userData.Platform);
            Assert.Single(userData.Roles);
            Assert.Equal("user", userData.Roles[0]);

            // Verify post data
            Assert.NotNull(capturedPostData);
            Assert.Equal("authorization_code", capturedPostData["grant_type"]);
            Assert.Equal(stateInfo.Code, capturedPostData["code"]);
            Assert.Equal(credential.ClientId, capturedPostData["client_id"]);
            Assert.Equal(credential.RedirectUrl, capturedPostData["redirect_uri"]);
            Assert.Contains("client_secret", capturedPostData.Keys);
        }

        [Fact]
        public async Task HandleSocialLogin_WithTokenError_ReturnsEmptyAppleUserDataAndLogsError()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "apple",
                Audience = "test-audience",
                Code = "invalid-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "apple",
                Audience = "test-audience",
                ClientId = "com.example.app",
                ClientSecret = "test-client-secret",
                AuthorizationUrl = "https://appleid.apple.com/auth/authorize",
                GetProfileUrl = "https://appleid.apple.com/auth/profile",
                Scope = "name email",
                TeamId = "TEAM123",
                KeyId = "KEY123",
                PrivateKey = GenerateTestECDsaKey(),
                AppleAudience = "https://appleid.apple.com",
                RedirectUrl = "https://example.com/oauth/callback",
                TokenUrl = "https://appleid.apple.com/auth/token"
            };

            var errorMessage = "Invalid authorization code";

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<SocialOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(((SocialOauthAccessToken)null, errorMessage));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<AppleUserData>(result);
            var userData = (AppleUserData)result;
            Assert.Null(userData.Email);
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

        [Fact]
        public async Task HandleSocialLogin_WithNullInitialRoles_ReturnsEmptyRolesArray()
        {
            // Arrange
            var stateInfo = new StateInfo
            {
                Provider = "apple",
                Audience = "test-audience",
                Code = "test-auth-code"
            };

            var credential = new SocialLoginCredential
            {
                Provider = "apple",
                Audience = "test-audience",
                ClientId = "com.example.app",
                TeamId = "TEAM123",
                KeyId = "KEY123",
                ClientSecret = "test-client-secret",
                PrivateKey = GenerateTestECDsaKey(),
                Scope = "name email",
                AppleAudience = "https://appleid.apple.com",
                RedirectUrl = "https://example.com/oauth/callback",
                TokenUrl = "https://appleid.apple.com/auth/token",
                InitialRoles = null,
                AuthorizationUrl = "https://appleid.apple.com/auth/authorize",
                GetProfileUrl = "https://appleid.apple.com/auth/profile",
            };

            var idToken = GenerateTestIdToken("user@example.com", "001234.abcd1234");

            var tokenResponse = new SocialOauthAccessToken
            {
                AccessToken = "test-access-token",
                IdToken = idToken
            };

            _authenticationRepository
                .Setup(x => x.GetSocialLoginCredentialByProvideAndAudienceAsync(stateInfo.Provider, stateInfo.Audience))
                .ReturnsAsync(credential);

            _httpService
                .Setup(x => x.SendFormUrlEncoded<SocialOauthAccessToken>(
                    HttpMethod.Post,
                    It.IsAny<Dictionary<string, string>>(),
                    credential.TokenUrl,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((tokenResponse, string.Empty));

            // Act
            var result = await _service.HandleSocialLogin(stateInfo);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<AppleUserData>(result);
            var userData = (AppleUserData)result;
            Assert.NotNull(userData.Roles);
            Assert.Empty(userData.Roles);
        }

        #endregion

        #region GenerateClientSecret Tests

        [Fact]
        public void GenerateClientSecret_WithValidCredentials_ReturnsValidJwtToken()
        {
            // Arrange
            
            var credential = new SocialLoginCredential
            {
                Provider = "apple",
                Audience = "test-audience",
                ClientId = "com.example.app",
                TeamId = "TEAM123",
                KeyId = "KEY123",
                ClientSecret = "test-client-secret",
                PrivateKey = GenerateTestECDsaKey(),
                Scope = "name email",
                AppleAudience = "https://appleid.apple.com",
                RedirectUrl = "https://example.com/oauth/callback",
                TokenUrl = "https://appleid.apple.com/auth/token",
                InitialRoles = null,
                AuthorizationUrl = "https://appleid.apple.com/auth/authorize",
                GetProfileUrl = "https://appleid.apple.com/auth/profile",
            };

            // Act
            var clientSecret = _service.GenerateClientSecret(credential);

            // Assert
            Assert.NotEmpty(clientSecret);
            
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(clientSecret);
            
            Assert.Equal("TEAM123", token.Payload["iss"]);
            Assert.Equal("com.example.app", token.Payload["sub"]);
            Assert.Equal("https://appleid.apple.com", token.Payload["aud"]);
            Assert.Contains("iat", token.Payload.Keys);
            Assert.Contains("exp", token.Payload.Keys);
            Assert.Equal("KEY123", token.Header.Kid);
            Assert.Equal(SecurityAlgorithms.EcdsaSha256, token.Header.Alg);
        }

        #endregion

        #region Helper Methods

        private string GenerateTestECDsaKey()
        {
            var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return ecdsa.ExportECPrivateKeyPem();
        }

        private string GenerateTestIdToken(string email, string sub)
        {
            var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = "TEST" };
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

            var payload = new JwtPayload
            {
                { "email", email },
                { "sub", sub },
                { "iss", "https://appleid.apple.com" },
                { "aud", "com.example.app" },
                { "exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() },
                { "iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
            };

            var header = new JwtHeader(credentials);
            var token = new JwtSecurityToken(header, payload);
            
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        #endregion
    }
}