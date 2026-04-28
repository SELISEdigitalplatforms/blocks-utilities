using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using System.IdentityModel.Tokens.Jwt;

namespace XUnitTest.DomainService.OAuth
{
    public class OAuthJwtAccessTokenManagerTests
    {
        private readonly Mock<IJwtAccessTokenProvider> _jwtAccessTokenProvider;
        private readonly Mock<IAuthenticationDomainService> _authenticationDomainService;
        private readonly Mock<IOtpServiceFactory> _otpServiceFactory;
        private readonly Mock<IMfaConfigurationService> _configurationService;
        private readonly Mock<IConfiguration> _configuration;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<ITenants> _tenants;
        private readonly OAuthJwtAccessTokenManager _manager;

        public OAuthJwtAccessTokenManagerTests()
        {
            _jwtAccessTokenProvider = new Mock<IJwtAccessTokenProvider>();
            _authenticationDomainService = new Mock<IAuthenticationDomainService>();
            _otpServiceFactory = new Mock<IOtpServiceFactory>();
            _configurationService = new Mock<IMfaConfigurationService>();
            _configuration = new Mock<IConfiguration>();
            _cacheClient = new Mock<ICacheClient>();
            _tenants = new Mock<ITenants>();

            _manager = new OAuthJwtAccessTokenManager(
                _jwtAccessTokenProvider.Object,
                _authenticationDomainService.Object,
                _configurationService.Object,
                _cacheClient.Object,
                _tenants.Object,
                _otpServiceFactory.Object,
                _configuration.Object
            );
        }

        [Fact]
        public async Task ManageTokenAsync_WithMfaEnabled_ReturnsMfaResponse()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.Password);
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: true);
            var mfaConfig = new Configuration { UserMfaType = new List<UserMfaType> { UserMfaType.Email } };
            var otpService = new Mock<IOtpService>();

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(mfaConfig);
            _otpServiceFactory.Setup(x => x.GetOTPService(UserMfaType.Email)).Returns(otpService.Object);
            otpService.Setup(x => x.GenerateAsync(It.IsAny<UserInfo>(), It.IsAny<string>())).ReturnsAsync(new OtpGenerationResponse { MfaId = "mfa-123" });

            var result = await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            Assert.Equal("mfa_enabled", result.Error);
            Assert.Equal("mfa-123", result.MfaId);
            Assert.Equal(UserMfaType.Email, result.UserMfa);
        }

        [Fact]
        public async Task ManageTokenAsync_WithoutMfa_ReturnsTokenResponse()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.Password);
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: false);
            var tenant = CreateTenant();
            var jwtToken = CreateJwtAccessToken();
            var mfaConfig = new Configuration { UserMfaType = new List<UserMfaType>() };

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(mfaConfig);
            _tenants.Setup(x => x.GetTenantByID(It.IsAny<string>())).Returns(tenant);
            _jwtAccessTokenProvider.Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, It.IsAny<string>())).ReturnsAsync(jwtToken);
            _authenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "127.0.0.1" });
            _authenticationDomainService.Setup(x => x.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation?)null);
            _authenticationDomainService.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(Task.FromResult(true));

            var result = await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            Assert.NotNull(result.AccessToken);
            Assert.NotNull(result.RefreshToken);
            Assert.Equal(200, result.StatusCode);
        }

        [Fact]
        public async Task ManageTokenAsync_WithAuthCodeGrant_SetsCustomIssuer()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.AuthCode);
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: false);
            var tenant = CreateTenant();
            var jwtToken = CreateJwtAccessToken();
            var mfaConfig = new Configuration { UserMfaType = new List<UserMfaType>() };

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(mfaConfig);
            _tenants.Setup(x => x.GetTenantByID(It.IsAny<string>())).Returns(tenant);
            _configuration.Setup(x => x["OpenIdConnect:IssuerUri"]).Returns("https://issuer.example.com");
            _jwtAccessTokenProvider.Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, It.IsAny<string>())).ReturnsAsync(jwtToken);
            _authenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "127.0.0.1" });
            _authenticationDomainService.Setup(x => x.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation?)null);
            _authenticationDomainService.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(Task.FromResult(true));

            await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            Assert.Equal("https://issuer.example.com", jwtToken.Issuer);
        }

        [Fact]
        public async Task ManageTokenAsync_WithMfaCodeGrant_SkipsMfaCheck()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.MfaCode);
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: true);
            var tenant = CreateTenant();
            var jwtToken = CreateJwtAccessToken();
            var mfaConfig = new Configuration { UserMfaType = new List<UserMfaType> { UserMfaType.Email } };

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(mfaConfig);
            _tenants.Setup(x => x.GetTenantByID(It.IsAny<string>())).Returns(tenant);
            _jwtAccessTokenProvider.Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, It.IsAny<string>())).ReturnsAsync(jwtToken);
            _authenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "127.0.0.1" });
            _authenticationDomainService.Setup(x => x.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation?)null);
            _authenticationDomainService.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(Task.FromResult(true));

            var result = await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            Assert.Null(result.Error);
            Assert.NotNull(result.AccessToken);
        }

        [Fact]
        public async Task ManageTokenAsync_WithRememberMe_ExtendsRefreshTokenLifetime()
        {
            var tokenRequest = CreateTokenRequest(GrantTypes.Password);
            tokenRequest.RememberMe = true;
            var authConfig = CreateAuthConfig();
            var user = CreateUser(mfaEnabled: false);
            var tenant = CreateTenant();
            var jwtToken = CreateJwtAccessToken();
            var mfaConfig = new Configuration { UserMfaType = new List<UserMfaType>() };

            _configurationService.Setup(x => x.GetAsync()).ReturnsAsync(mfaConfig);
            _tenants.Setup(x => x.GetTenantByID(It.IsAny<string>())).Returns(tenant);
            _jwtAccessTokenProvider.Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, It.IsAny<string>())).ReturnsAsync(jwtToken);
            _authenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(new List<string> { "127.0.0.1" });
            _authenticationDomainService.Setup(x => x.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation?)null);
            _authenticationDomainService.Setup(x => x.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _cacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(Task.FromResult(true));

            var result = await _manager.ManageTokenAsync(tokenRequest, authConfig, user);

            _cacheClient.Verify(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 10080 * 60), Times.Once);
        }

        [Fact]
        public void CreateJwtAccessToken_CreatesValidToken()
        {
            var jwtToken = CreateJwtAccessToken();

            var token = OAuthJwtAccessTokenManager.CreateJwtAccessToken(jwtToken);

            Assert.NotNull(token);
            var handler = new JwtSecurityTokenHandler();
            Assert.True(handler.CanReadToken(token));
        }

        [Fact]
        public void ProcessAccountLock_WhenNotLocked_ReturnsEmptyResponse()
        {
            var authConfig = CreateAuthConfig();
            var tenant = CreateTenant();
            var user = CreateUser(mfaEnabled: false);

            _cacheClient.Setup(x => x.GetStringValue(It.IsAny<string>())).Returns((string)null);

            var result = _manager.ProcessAccountLock(authConfig, tenant, user);

            Assert.Null(result.Error);
        }

        [Fact]
        public void ProcessAccountLock_WhenLocked_ReturnsLockedError()
        {
            var authConfig = CreateAuthConfig();
            authConfig.GetNumberOfWrongAttemptsToLockTheAccount = 3;
            var tenant = CreateTenant();
            var user = CreateUser(mfaEnabled: false);

            _cacheClient.Setup(x => x.GetStringValue(It.IsAny<string>())).Returns("3");

            var result = _manager.ProcessAccountLock(authConfig, tenant, user);

            Assert.Equal(OAuthError.AccountLocked, result.Error);
        }

        [Fact]
        public void Lock_IncrementsLockCount()
        {
            _cacheClient.Setup(x => x.GetStringValue(It.IsAny<string>())).Returns("1");

            _manager.Lock("test-key", 30, 5);

            _cacheClient.Verify(x => x.AddStringValue("test-key", "2", 1800), Times.Once);
        }

        [Fact]
        public void Lock_WhenMaxAttemptsReached_DoesNotIncrement()
        {
            _cacheClient.Setup(x => x.GetStringValue(It.IsAny<string>())).Returns("5");

            _manager.Lock("test-key", 30, 5);

            _cacheClient.Verify(x => x.AddStringValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void IsLocked_WhenBelowMaxAttempts_ReturnsFalse()
        {
            _cacheClient.Setup(x => x.GetStringValue(It.IsAny<string>())).Returns("2");

            var result = _manager.IsLocked("test-key", 5);

            Assert.False(result);
        }

        [Fact]
        public void IsLocked_WhenAtOrAboveMaxAttempts_ReturnsTrue()
        {
            _cacheClient.Setup(x => x.GetStringValue(It.IsAny<string>())).Returns("5");

            var result = _manager.IsLocked("test-key", 5);

            Assert.True(result);
        }

        [Fact]
        public void IsLocked_WhenNoCache_ReturnsFalse()
        {
            _cacheClient.Setup(x => x.GetStringValue(It.IsAny<string>())).Returns((string)null);

            var result = _manager.IsLocked("test-key", 5);

            Assert.False(result);
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

        private AuthenticationConfiguration CreateAuthConfig() => new()
        {
            AccessTokenValidForNumberMinutes = 15,
            RefreshTokenValidForNumberMinutes = 1440,
            RememberMeRefreshTokenValidForNumberMinutes = 10080,
            AccountLockDurationInMinutes = 30,
            GetNumberOfWrongAttemptsToLockTheAccount = 5
        };

        private User CreateUser(bool mfaEnabled) => new()
        {
            ItemId = "user-123",
            Email = "test@example.com",
            MfaEnabled = mfaEnabled,
            UserMfaType = UserMfaType.Email,
            Language = "en-US",
            OrganizationIds = new List<string> { "org-1" }
        };

        private Tenant CreateTenant() => new()
        {
            TenantId = "tenant-123",
            CookieDomain = ".example.com",
            ApplicationDomain = "app.example.com",
            DbConnectionString = "test-connection-string",
            JwtTokenParameters = new JwtTokenParameters()
            {
                PrivateCertificatePassword = "test-password",
                IssueDate = DateTime.UtcNow
            }
        };

        private JwtAccessToken CreateJwtAccessToken() => new()
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Claims = new List<System.Security.Claims.Claim>(),
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = null
        };
    }
}