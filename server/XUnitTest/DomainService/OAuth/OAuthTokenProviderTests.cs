using DomainService.Dtos;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;

namespace XUnitTest.DomainService.OAuth
{
    public class OAuthTokenProviderTests
    {
        private readonly Mock<ILogger<OAuthTokenProvider>> _logger;
        private readonly Mock<IAuthenticationRepository> _repository;
        private readonly Mock<IConfiguration> _configuration;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<ISocialLogInServiceProvider> _socialLogInServiceProvider;
        private readonly Mock<ITenants> _tenants;
        private readonly Mock<ITokenService> _tokenService;
        private readonly OAuthTokenProvider _provider;
        private readonly IServiceProvider _serviceProvider;

        public OAuthTokenProviderTests()
        {
            _logger = new Mock<ILogger<OAuthTokenProvider>>();
            _repository = new Mock<IAuthenticationRepository>();
            _configuration = new Mock<IConfiguration>();
            _cacheClient = new Mock<ICacheClient>();
            _socialLogInServiceProvider = new Mock<ISocialLogInServiceProvider>();
            _tenants = new Mock<ITenants>();
            _tokenService = new Mock<ITokenService>();

            var services = new ServiceCollection();
            services.AddTransient(_ => _tokenService.Object);
            _serviceProvider = services.BuildServiceProvider();

            _provider = new OAuthTokenProvider(_logger.Object, _serviceProvider, _repository.Object, 
                _configuration.Object, _cacheClient.Object, _socialLogInServiceProvider.Object, _tenants.Object);

            SetupBlocksContext();
        }

        [Fact]
        public async Task AuthenticateAsync_ConfigNotFound_ReturnsInvalidRequest()
        {
            var request = CreateTokenRequest(GrantTypes.Password);
            _repository.Setup(x => x.GetAuthenticationConfigurationAsync()).ReturnsAsync((AuthenticationConfiguration)null);

            var result = await _provider.AuthenticateAsync(request);

            var objectResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, objectResult.StatusCode);
        }

        [Fact]
        public async Task AuthenticateAsync_GrantTypeNotAllowed_ReturnsInvalidRequest()
        {
            var request = CreateTokenRequest(GrantTypes.Password);
            var config = CreateAuthConfig(new[] { GrantTypes.AuthCode });
            _repository.Setup(x => x.GetAuthenticationConfigurationAsync()).ReturnsAsync(config);

            var result = await _provider.AuthenticateAsync(request);

            var objectResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, objectResult.StatusCode);
        }

        [Fact]
        public async Task AuthenticateAsync_UnsupportedGrantType_ReturnsUnsupportedGrantType()
        {
            var request = CreateTokenRequest("unsupported");
            var config = CreateAuthConfig(new[] { "unsupported" });
            _repository.Setup(x => x.GetAuthenticationConfigurationAsync()).ReturnsAsync(config);

            var result = await _provider.AuthenticateAsync(request);

            var objectResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, objectResult.StatusCode);
        }
        
        [Theory]
        [InlineData(OAuthError.MfaEnabled)]
        [InlineData(OAuthError.CaptchaEnabled)]
        public async Task HandleAuthenticationAsync_SpecialErrors_ReturnsAppropriateResponse(string errorType)
        {
            var request = CreateTokenRequest(GrantTypes.Password);
            var config = CreateAuthConfig(new[] { GrantTypes.Password });
            var tokenResponse = new TokenResponse { Error = errorType, StatusCode = 200 };
            _tokenService.Setup(x => x.AuthenticateAsync(request, config, null)).ReturnsAsync(tokenResponse);

            var result = await _provider.HandleAuthenticationAsync(_tokenService.Object, request, config);

            var objectResult = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal(401, objectResult.StatusCode);
        }

        [Theory]
        [InlineData("invalid_grant", 401)]
        public async Task HandleAuthenticationAsync_Errors_ReturnsErrorResponse(string error, int statusCode)
        {
            var request = CreateTokenRequest(GrantTypes.Password);
            var config = CreateAuthConfig(new[] { GrantTypes.Password });
            var tokenResponse = new TokenResponse { Error = error, StatusCode = statusCode, ErrorDescription = "Test error" };
            _tokenService.Setup(x => x.AuthenticateAsync(request, config, null)).ReturnsAsync(tokenResponse);

            var result = await _provider.HandleAuthenticationAsync(_tokenService.Object, request, config);

            var objectResult = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal(statusCode, objectResult.StatusCode);
        }

        [Fact]
        public void GetCookieOptions_ReturnsCorrectOptions()
        {
            var domain = "test.com";
            var expires = DateTime.UtcNow.AddHours(1);
            var configSection = new Mock<IConfigurationSection>();
            configSection.Setup(x => x.Value).Returns("true");
            _configuration.Setup(x => x.GetSection("SecureCookieOptions")).Returns(configSection.Object);

            var result = _provider.GetCookieOptions(domain, expires);

            Assert.Equal(domain, result.Domain);
            Assert.Equal(expires, result.Expires);
            Assert.True(result.HttpOnly);
            Assert.True(result.Secure);
            Assert.Equal(SameSiteMode.None, result.SameSite);
        }

        [Fact]
        public async Task GetTokenResponse_InvalidRequest_ReturnsInvalidResponse()
        {
            var request = CreateTokenRequest(GrantTypes.Password);
            request.Username = null;
            var config = CreateAuthConfig(new[] { GrantTypes.Password });

            var result = await _provider.GetTokenResponse(_tokenService.Object, request, config);

            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task GetTokenResponse_ValidRequest_CallsAuthService()
        {
            var request = CreateTokenRequest(GrantTypes.Password);
            request.Username = "testuser";
            var config = CreateAuthConfig(new[] { GrantTypes.Password });
            var tokenResponse = CreateTokenResponse(true);
            _tokenService.Setup(x => x.AuthenticateAsync(request, config, null)).ReturnsAsync(tokenResponse);

            var result = await _provider.GetTokenResponse(_tokenService.Object, request, config);

            Assert.NotNull(result.AccessToken);
            _tokenService.Verify(x => x.AuthenticateAsync(request, config, null), Times.Once);
        }

        [Theory]
        [InlineData(GrantTypes.Password, "username", null, null, null, null, null, null, null, true)]
        [InlineData(GrantTypes.Password, null, null, null, null, null, null, null, null, false)]
        [InlineData(GrantTypes.MfaCode, null, "code", "mfaId", UserMfaType.Email, null, null, null, null, true)]
        [InlineData(GrantTypes.MfaCode, null, "code", "mfaId", UserMfaType.None, null, null, null, null, false)]
        [InlineData(GrantTypes.Social, null, "code", null, null, "state", null, null, null, true)]
        [InlineData(GrantTypes.Social, null, "code", null, null, null, null, null, null, false)]
        [InlineData(GrantTypes.AuthCode, null, "code", null, null, null, null, null, null, true)]
        [InlineData(GrantTypes.BiometricAuthorization, null, null, null, null, null, "bioId", "bioKey", null, true)]
        [InlineData(GrantTypes.BiometricAuthorization, null, null, null, null, null, "bioId", null, null, false)]
        [InlineData(GrantTypes.ClientCredential, null, null, null, null, null, null, null, "clientId:clientSecret", true)]
        [InlineData(GrantTypes.ClientUserCode, null, null, null, null, null, null, null, "clientId:userCode", true)]
        [InlineData(GrantTypes.SwitchOrganization, null, null, null, null, null, null, null, null, false)]
        public async Task GetTokenResponse_ValidatesRequestByGrantType(string grantType, string username, string code, 
            string mfaId, UserMfaType mfaType, string state, string bioId, string bioKey, string clientData, bool isValid)
        {
            var request = CreateTokenRequest(grantType);
            request.Username = username;
            request.Code = code;
            request.MfaId = mfaId;
            request.MfaType = mfaType;
            request.State = state;
            request.BiometricId = bioId;
            request.BiometricKey = bioKey;
            
            if (clientData != null)
            {
                var parts = clientData.Split(':');
                request.ClientId = parts[0];
                if (grantType == GrantTypes.ClientCredential)
                    request.ClientSecret = parts[1];
                else
                    request.UserCode = parts[1];
            }

            var config = CreateAuthConfig(new[] { grantType });
            _tokenService.Setup(x => x.AuthenticateAsync(request, config, null)).ReturnsAsync(CreateTokenResponse(true));

            var result = await _provider.GetTokenResponse(_tokenService.Object, request, config);

            if (isValid)
                _tokenService.Verify(x => x.AuthenticateAsync(request, config, null), Times.Once);
            else
                Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task GetTokenResponseForRefreshToken_CookieNotFound_ReturnsError()
        {
            var request = CreateTokenRequest(GrantTypes.RefreshToken);
            var config = CreateAuthConfig(new[] { GrantTypes.RefreshToken });

            var result = await _provider.GetTokenResponseForRefreshToken(_tokenService.Object, request, config);

            Assert.Equal(OAuthError.RefreshTokenCookieNotFound, result.Error);
        }

        [Fact]
        public async Task GetTokenResponseForRefreshToken_CacheNotFound_ReturnsInvalidToken()
        {
            var request = CreateTokenRequest(GrantTypes.RefreshToken);
            request.RefreshToken = "test-token";
            var config = CreateAuthConfig(new[] { GrantTypes.RefreshToken });
            _cacheClient.Setup(x => x.GetStringValueAsync("test-token")).ReturnsAsync((string)null);

            var result = await _provider.GetTokenResponseForRefreshToken(_tokenService.Object, request, config);

            Assert.Equal(OAuthError.InvalidRefreshToken, result.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("{}")]
        [InlineData("{\"RefreshToken\":\"\"}")]
        [InlineData("{\"UserId\":\"\"}")]
        public async Task GetTokenResponseForRefreshToken_InvalidCacheData_ReturnsInvalidToken(string cacheData)
        {
            var request = CreateTokenRequest(GrantTypes.RefreshToken);
            request.RefreshToken = "test-token";
            var config = CreateAuthConfig(new[] { GrantTypes.RefreshToken });
            _cacheClient.Setup(x => x.GetStringValueAsync("test-token")).ReturnsAsync(cacheData);

            var result = await _provider.GetTokenResponseForRefreshToken(_tokenService.Object, request, config);

            Assert.Equal(OAuthError.InvalidRefreshToken, result.Error);
        }

        [Fact]
        public async Task GetTokenResponseForRefreshToken_UserNotFound_ReturnsInvalidToken()
        {
            var request = CreateTokenRequest(GrantTypes.RefreshToken);
            request.RefreshToken = "test-token";
            var config = CreateAuthConfig(new[] { GrantTypes.RefreshToken });
            var cacheData = JsonSerializer.Serialize(new RefreshTokenCache { RefreshToken = "test-token", UserId = "user-123" });
            _cacheClient.Setup(x => x.GetStringValueAsync("test-token")).ReturnsAsync(cacheData);
            _repository.Setup(x => x.GetUserByIdAsync("user-123")).ReturnsAsync((User)null);

            var result = await _provider.GetTokenResponseForRefreshToken(_tokenService.Object, request, config);

            Assert.Equal(OAuthError.InvalidRefreshToken, result.Error);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task GetTokenResponseForRefreshToken_UserNotActiveOrVerified_ReturnsError(bool active, bool verified)
        {
            var request = CreateTokenRequest(GrantTypes.RefreshToken);
            request.RefreshToken = "test-token";
            var config = CreateAuthConfig(new[] { GrantTypes.RefreshToken });
            var user = CreateUser(active, verified);
            var cacheData = JsonSerializer.Serialize(new RefreshTokenCache { RefreshToken = "test-token", UserId = "user-123" });
            _cacheClient.Setup(x => x.GetStringValueAsync("test-token")).ReturnsAsync(cacheData);
            _repository.Setup(x => x.GetUserByIdAsync("user-123")).ReturnsAsync(user);

            var result = await _provider.GetTokenResponseForRefreshToken(_tokenService.Object, request, config);

            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task GetTokenResponseForRefreshToken_InvalidOrganization_ReturnsError()
        {
            var request = CreateTokenRequest(GrantTypes.SwitchOrganization);
            request.RefreshToken = "test-token";
            request.OrganizationId = "invalid-org";
            var config = CreateAuthConfig(new[] { GrantTypes.SwitchOrganization });
            var user = CreateUser(true, true);
            var cacheData = JsonSerializer.Serialize(new RefreshTokenCache { RefreshToken = "test-token", UserId = "user-123" });
            _cacheClient.Setup(x => x.GetStringValueAsync("test-token")).ReturnsAsync(cacheData);
            _repository.Setup(x => x.GetUserByIdAsync("user-123")).ReturnsAsync(user);

            var result = await _provider.GetTokenResponseForRefreshToken(_tokenService.Object, request, config);

            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task GetTokenResponseForRefreshToken_Success_ReturnsToken()
        {
            var request = CreateTokenRequest(GrantTypes.RefreshToken);
            request.RefreshToken = "test-token";
            var config = CreateAuthConfig(new[] { GrantTypes.RefreshToken });
            var user = CreateUser(true, true);
            var cacheData = JsonSerializer.Serialize(new RefreshTokenCache { RefreshToken = "test-token", UserId = "user-123" });
            var tokenResponse = CreateTokenResponse(true);
            _cacheClient.Setup(x => x.GetStringValueAsync("test-token")).ReturnsAsync(cacheData);
            _repository.Setup(x => x.GetUserByIdAsync("user-123")).ReturnsAsync(user);
            _tokenService.Setup(x => x.AuthenticateAsync(request, config, user)).ReturnsAsync(tokenResponse);

            var result = await _provider.GetTokenResponseForRefreshToken(_tokenService.Object, request, config);

            Assert.NotNull(result.AccessToken);
            _tokenService.Verify(x => x.AuthenticateAsync(request, config, user), Times.Once);
        }

        [Fact]
        public async Task GetSocialLogInEndPointAsync_CallsProvider()
        {
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "test-provider",
                Audience = "test-audience"
            };

            var expectedResponse = new GetSocialLogInEndPointResponse();
            _socialLogInServiceProvider.Setup(x => x.GetSocialLogInEndPointAsync(request)).ReturnsAsync(expectedResponse);

            var result = await _provider.GetSocialLogInEndPointAsync(request);

            Assert.Equal(expectedResponse, result);
        }

        #region Helper Methods

        private void SetupBlocksContext()
        {
            var context = BlocksContext.Create("tenant-123", null, null, false, null, null, 
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", "tenant-123");
            BlocksContext.SetContext(context);
            _tenants.Setup(x => x.GetTenantByID("tenant-123")).Returns(new Tenant 
            { 
                TenantId = "tenant-123", 
                CookieDomain = ".example.com",
                ApplicationDomain = "test.example.com",
                DbConnectionString = "test-connection-string",
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = "test-password",
                    IssueDate = DateTime.UtcNow
                }
            });
        }

        private TokenRequest CreateTokenRequest(string grantType)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.UserAgent = "Test Agent";
            return new TokenRequest
            {
                GrantType = grantType,
                Request = context.Request,
                Scope = "openid profile"
            };
        }

        private AuthenticationConfiguration CreateAuthConfig(string[] allowedGrantTypes) => new()
        {
            AllowedGrantTypes = allowedGrantTypes.ToList(),
            AccessTokenValidForNumberMinutes = 15,
            RefreshTokenValidForNumberMinutes = 1440
        };

        private TokenResponse CreateTokenResponse(bool withRefreshToken) => new()
        {
            AccessToken = "access-token",
            RefreshToken = withRefreshToken ? "refresh-token" : null,
            ExpiresIn = 15,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(15),
            RefreshExpiresUtc = withRefreshToken ? DateTime.UtcNow.AddDays(1) : default,
            CookieDomain = ".example.com",
            StatusCode = 200
        };

        private User CreateUser(bool active, bool verified) => new()
        {
            ItemId = "user-123",
            Active = active,
            IsVarified = verified,
            Memberships = new List<OrganizationMembership>
            {
                new() { OrganizationId = "org-123" }
            }
        };

        #endregion
    }
}