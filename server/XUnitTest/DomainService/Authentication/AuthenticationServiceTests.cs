using Blocks.Genesis;
using DomainService.Authentication;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Services;
using DomainService.Shared.RequestModel;
using DomainService.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.DomainService.Authentication
{
    public class AuthenticationServiceTests
    {
        private readonly Mock<ILogger<AuthenticationService>> _mockLogger;
        private readonly Mock<ICacheClient> _mockCacheClient;
        private readonly Mock<IAuthenticationRepository> _mockAuthenticationRepository;
        private readonly Mock<IAuthenticationDomainService> _mockAuthenticationDomainService;
        private readonly Mock<ITenants> _mockTenants;
        private readonly Mock<IDatabase> _mockDatabase;
        private readonly AuthenticationService _service;

        public AuthenticationServiceTests()
        {
            _mockLogger = new Mock<ILogger<AuthenticationService>>();
            _mockCacheClient = new Mock<ICacheClient>();
            _mockAuthenticationRepository = new Mock<IAuthenticationRepository>();
            _mockAuthenticationDomainService = new Mock<IAuthenticationDomainService>();
            _mockTenants = new Mock<ITenants>();
            _mockDatabase = new Mock<IDatabase>();

            _mockCacheClient.Setup(x => x.CacheDatabase()).Returns(_mockDatabase.Object);

            _service = new AuthenticationService(
                _mockLogger.Object,
                _mockCacheClient.Object,
                _mockAuthenticationRepository.Object,
                _mockAuthenticationDomainService.Object,
                _mockTenants.Object);
        }

        [Fact]
        public async Task ProcessLogout_RemovesCache_AndUpdatesSession()
        {
            var token = "test-refresh-token";

            _mockAuthenticationRepository.Setup(x => x.UpdateSessionStatusAsync(token, It.IsAny<string>()))
                 .ReturnsAsync(true);

            var result = await _service.ProcessLogout(token);

            _mockCacheClient.Verify(x => x.RemoveKeyAsync(token), Times.Once);
            _mockAuthenticationRepository.Verify(x => x.UpdateSessionStatusAsync(token, It.IsAny<string>()), Times.Once);

            Assert.True(result);
        }

        [Fact]
        public async Task ProcessLogoutAll_RemovesAllTokens_AndUpdatesSessions()
        {
            // Arrange
            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant-id",
                roles: Array.Empty<string>(),
                userId: "test-user-id",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@example.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant-id"
            );

            BlocksContext.SetContext(blocksContext, true);

            var sessions = new List<Session>
            {
                new Session { RefreshToken = "r1" },
                new Session { RefreshToken = "r2" }
            };

            _mockAuthenticationRepository.Setup(x => x.GetActiveSessionByUserIdAsync(It.IsAny<string>()))
                 .ReturnsAsync(sessions);

            _mockAuthenticationRepository.Setup(x => x.UpdateSessionStatusForAllRefreshTokenAsync(
                It.IsAny<List<string>>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ProcessLogoutAll();

            // Assert
            _mockCacheClient.Verify(x => x.RemoveKeyAsync("r1"), Times.Once);
            _mockCacheClient.Verify(x => x.RemoveKeyAsync("r2"), Times.Once);

            Assert.True(result);

            // Cleanup
            BlocksContext.ClearContext();
        }

        [Fact]
        public async Task ConstructRedirectUriAsync_ReturnsCorrectUri_AndCachesState()
        {
            var client = new OIDCClientCredential
            {
                RedirectUri = "https://client.com/callback",
                Audience = "aud"
            };

            _mockAuthenticationRepository.Setup(x => x.GetOIDCClientCredentialAsync("client1"))
                 .ReturnsAsync(client);

            var request = new AcknowledgeRequest
            {
                ClientId = "client1",
                Scope = "openid",
                State = "abc",
                Nonce = "nonce",
                Username = "user"
            };

            var uri = await _service.ConstructRedirectUriAsync("client1", request);

            _mockCacheClient.Verify(x => x.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                300), Times.Once);

            Assert.Contains("https://client.com/callback?code=", uri);
            Assert.Contains("&state=abc", uri);
        }

        [Fact]
        public void CookieToken_Should_ReturnTokenFromCookie()
        {
            // Arrange
            var tenantId = "8656D85F-C3E0-48AA-9505-654505096AEC";
            var expectedToken = "refresh-token-value";
            var httpRequest = CreateMockHttpRequestWithCookie(tenantId, expectedToken);

            // Arrange
            var blocksContext = BlocksContext.Create(
                tenantId: tenantId,
                roles: Array.Empty<string>(),
                userId: "test-user-id",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@example.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: expectedToken,
                actualTentId: "test-tenant-id"
            );

            BlocksContext.SetContext(blocksContext, true);

            // Act
            var result = _service.CookieToken(httpRequest);

            // Assert
            result.Should().Be(expectedToken);
        }

        [Fact]
        public void DeleteCookie_Should_DeleteBothCookies()
        {
            // Arrange
            var tenantId = "8656D85F-C3E0-48AA-9505-654505096AEC";
            var cookieDomain = "example.com";
            var httpContext = new DefaultHttpContext();
            var httpRequest = httpContext.Request;

            var tenant = new Tenant
            {
                TenantId = tenantId,
                CookieDomain = cookieDomain,
                ApplicationDomain = "test.example.com",
                DbConnectionString = "test-connection-string",
                JwtTokenParameters = new JwtTokenParameters()
                {
                    PrivateCertificatePassword = "test-private-cert-password",
                    IssueDate = DateTime.UtcNow
                }
            };

            var blocksContext = BlocksContext.Create(
                tenantId: tenantId,
                roles: Array.Empty<string>(),
                userId: "test-user-id",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@example.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken:string.Empty,
                actualTentId: "test-tenant-id"
            );

            BlocksContext.SetContext(blocksContext, true);

            _mockTenants.Setup(x => x.GetTenantByID(tenantId))
                .Returns(tenant);

            // Act
            var result = _service.DeleteCookie(httpRequest);

            // Assert
            result.Should().BeTrue();
            _mockTenants.Verify(x => x.GetTenantByID(tenantId), Times.Once);
        }

        private HttpRequest CreateMockHttpRequest()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.UserAgent = "Test User Agent";
            return context.Request;
        }

        private HttpRequest CreateMockHttpRequestWithCookie(string tenantId, string tokenValue)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.UserAgent = "Test User Agent";
            context.Request.Cookies = new MockRequestCookieCollection(
                new Dictionary<string, string>
                {
                    { $"{IdpConstants.RefreshTokenCookieName}_{tenantId}", tokenValue }
                });
            return context.Request;
        }

        private class MockRequestCookieCollection : IRequestCookieCollection
        {
            private readonly Dictionary<string, string> _cookies;

            public MockRequestCookieCollection(Dictionary<string, string> cookies)
            {
                _cookies = cookies;
            }

            public string this[string key] => _cookies.TryGetValue(key, out var value) ? value : null;
            public int Count => _cookies.Count;
            public ICollection<string> Keys => _cookies.Keys;
            public bool ContainsKey(string key) => _cookies.ContainsKey(key);
            public bool TryGetValue(string key, out string value) => _cookies.TryGetValue(key, out value);
            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _cookies.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        
        [Fact]
        public async Task LogoutUser_WithRefreshToken_Should_ProcessLogout()
        {
            // Arrange
            var refreshToken = "test-refresh-token";
            var httpRequest = CreateMockHttpRequest();

            _mockCacheClient.Setup(x => x.RemoveKeyAsync(refreshToken))
                .ReturnsAsync(true);
            _mockAuthenticationRepository.Setup(x => x.UpdateSessionStatusAsync(refreshToken, It.IsAny<string>()))
                .ReturnsAsync(true);
            _mockAuthenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>()))
                .Returns(new List<string> { "127.0.0.1" });

            // Act
            var result = await _service.LogoutUser(refreshToken, httpRequest);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            _mockCacheClient.Verify(x => x.RemoveKeyAsync(refreshToken), Times.Once);
            _mockAuthenticationRepository.Verify(x => x.UpdateSessionStatusAsync(refreshToken, It.IsAny<string>()), Times.Once);
        }

        private HttpRequest CreateMockHttpRequestWithHeader(string tenantId, string tokenValue)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.UserAgent = "Test User Agent";
            context.Request.Headers[$"{IdpConstants.RefreshTokenCookieName}_{tenantId}"] = tokenValue;
            return context.Request;
        }

        [Fact]
        public async Task LogoutUser_WithoutRefreshToken_Should_ProcessLogoutAll()
        {
            // Arrange
            var httpRequest = CreateMockHttpRequest();
            var refreshTokens = new List<Session>
            {
                new Session { RefreshToken = "token1" },
                new Session { RefreshToken = "token2" }
            };

            var blocksContext = BlocksContext.Create(
                tenantId: "8656D85F-C3E0-48AA-9505-654505096AEC",
                roles: Array.Empty<string>(),
                userId: "test-user-id",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@example.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant-id"
            );

            BlocksContext.SetContext(blocksContext, true);

            _mockAuthenticationRepository.Setup(x => x.GetActiveSessionByUserIdAsync(It.IsAny<string>()))
                .ReturnsAsync(refreshTokens);
            _mockCacheClient.Setup(x => x.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true);
            _mockAuthenticationRepository.Setup(x => x.UpdateSessionStatusForAllRefreshTokenAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(true);
            _mockAuthenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>()))
                .Returns(new List<string> { "127.0.0.1" });

            // Act
            var result = await _service.LogoutUser(string.Empty, httpRequest);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            _mockCacheClient.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ProcessLogout_Should_RemoveCacheAndUpdateSession()
        {
            // Arrange
            var refreshToken = "test-refresh-token";

            _mockCacheClient.Setup(x => x.RemoveKeyAsync(refreshToken))
                .ReturnsAsync(true);
            _mockAuthenticationRepository.Setup(x => x.UpdateSessionStatusAsync(refreshToken, It.IsAny<string>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ProcessLogout(refreshToken);

            // Assert
            result.Should().BeTrue();
            _mockCacheClient.Verify(x => x.RemoveKeyAsync(refreshToken), Times.Once);
            _mockAuthenticationRepository.Verify(x => x.UpdateSessionStatusAsync(refreshToken, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ProcessLogoutAll_Should_RemoveAllUserSessions()
        {
            // Arrange
            var refreshTokens = new List<Session>
            {
                new Session { RefreshToken = "token1" },
                new Session { RefreshToken = "token2" },
                new Session { RefreshToken = "token3" }
            };

            var blocksContext = BlocksContext.Create(
                tenantId: "8656D85F-C3E0-48AA-9505-654505096AEC",
                roles: Array.Empty<string>(),
                userId: "test-user-id",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@example.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant-id"
            );

            BlocksContext.SetContext(blocksContext, true);

            _mockAuthenticationRepository.Setup(x => x.GetActiveSessionByUserIdAsync(It.IsAny<string>()))
                .ReturnsAsync(refreshTokens);
            _mockCacheClient.Setup(x => x.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true);
            _mockAuthenticationRepository.Setup(x => x.UpdateSessionStatusForAllRefreshTokenAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ProcessLogoutAll();

            // Assert
            result.Should().BeTrue();
            _mockCacheClient.Verify(x => x.RemoveKeyAsync(It.IsAny<string>()), Times.Exactly(3));
            _mockAuthenticationRepository.Verify(x => x.UpdateSessionStatusForAllRefreshTokenAsync(
                It.Is<List<string>>(list => list.Count == 3)), Times.Once);
        }

        [Fact]
        public async Task ProcessTimeline_Should_SendEventToQueue()
        {
            // Arrange
            var httpRequest = CreateMockHttpRequest();
            var isFromAll = true;

            _mockAuthenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>()))
                .Returns(new List<string> { "127.0.0.1", "192.168.1.1" });

            // Act
            var result = await _service.ProcessTimeline(httpRequest, isFromAll);

            // Assert
            result.Should().BeTrue();
            _mockAuthenticationDomainService.Verify(x => x.SendToQueueAsync(
                IdpConstants.AuthenticationQueue,
                It.Is<UserAuthenticationTimelineEvent>(e =>
                    e.Event == "revoke_access_by_logout_all" &&
                    e.ActionBy == "call_api_to_logout_all")), Times.Once);
        }

        [Fact]
        public async Task ProcessTimeline_WithSingleLogout_Should_SendCorrectEvent()
        {
            // Arrange
            var httpRequest = CreateMockHttpRequest();
            var isFromAll = false;

            _mockAuthenticationDomainService.Setup(x => x.GetVisitorsIpAddresses(It.IsAny<HttpContext>()))
                .Returns(new List<string> { "10.0.0.1" });


            // Act
            var result = await _service.ProcessTimeline(httpRequest, isFromAll);

            // Assert
            result.Should().BeTrue();
            _mockAuthenticationDomainService.Verify(x => x.SendToQueueAsync(
                IdpConstants.AuthenticationQueue,
                It.Is<UserAuthenticationTimelineEvent>(e =>
                    e.Event == "revoke_access_by_logout" &&
                    e.ActionBy == "call_api_to_logout")), Times.Once);
        }

        [Fact]
        public async Task GetClientCredentialAsync_Should_ReturnCredentials()
        {
            // Arrange
            var clientId = "test-client-id";
            var expectedCredential = new OIDCClientCredential
            {
                ClientSecret = "secret",
                Audience = "test-audience",
                RedirectUri = "https://example.com/callback"
            };

            _mockAuthenticationRepository.Setup(x => x.GetOIDCClientCredentialAsync(clientId))
                .ReturnsAsync(expectedCredential);

            // Act
            var result = await _service.GetClientCredentialAsync(clientId);

            // Assert
            result.Should().NotBeNull();
            result.Audience.Should().Be("test-audience");
        }

        [Fact]
        public async Task ConstructRedirectUriAsync_Should_ReturnValidUri()
        {
            // Arrange
            var clientId = "test-client-id";
            var request = new AcknowledgeRequest
            {
                ClientId = clientId,
                Scope = "openid profile",
                State = "test-state",
                Nonce = "test-nonce",
                Username = "testuser"
            };
            var client = new OIDCClientCredential
            {
                ClientSecret = "secret",
                RedirectUri = "https://example.com/callback",
                Audience = "test-audience"
            };

            _mockAuthenticationRepository.Setup(x => x.GetOIDCClientCredentialAsync(clientId))
                .ReturnsAsync(client);
            _mockCacheClient.Setup(x => x.AddStringValueAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ConstructRedirectUriAsync(clientId, request);

            // Assert
            result.Should().StartWith("https://example.com/callback?code=");
            result.Should().Contain("&state=test-state");
            _mockCacheClient.Verify(x => x.AddStringValueAsync(
                It.IsAny<string>(),
                It.Is<string>(s => s.Contains("test-state") && s.Contains("test-nonce")),
                300), Times.Once);
        }

        [Fact]
        public async Task ConstructRedirectUriAsync_WithoutState_Should_ReturnUriWithoutState()
        {
            // Arrange
            var clientId = "test-client-id";
            var request = new AcknowledgeRequest
            {
                ClientId = clientId,
                Scope = "openid",
                State = null,
                Username = "testuser"
            };
            var client = new OIDCClientCredential
            {
                ClientSecret = "secret",
                RedirectUri = "https://example.com/callback",
                Audience = "test-audience"
            };

            _mockAuthenticationRepository.Setup(x => x.GetOIDCClientCredentialAsync(clientId))
                .ReturnsAsync(client);
            _mockCacheClient.Setup(x => x.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ConstructRedirectUriAsync(clientId, request);

            // Assert
            result.Should().StartWith("https://example.com/callback?code=");
            result.Should().NotContain("&state=");
        }

        [Fact]
        public void CookieToken_Should_ReturnTokenFromHeader_WhenCookieIsEmpty()
        {
            // Arrange
            var tenantId = "8656D85F-C3E0-48AA-9505-654505096AEC";
            var expectedToken = "header-token-value";
            var httpRequest = CreateMockHttpRequestWithHeader(tenantId, expectedToken);

            var blocksContext = BlocksContext.Create(
                tenantId: "8656D85F-C3E0-48AA-9505-654505096AEC",
                roles: Array.Empty<string>(),
                userId: "test-user-id",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@example.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant-id"
            );

            BlocksContext.SetContext(blocksContext, true);

            // Act
            var result = _service.CookieToken(httpRequest);

            // Assert
            result.Should().Be(expectedToken);
        }

        [Fact]
        public async Task GetLoginOptionsAsync_Should_ReturnAllowedGrantTypes()
        {
            // Arrange
            var config = new AuthenticationConfiguration
            {
                AllowedGrantTypes = new List<string> { "password", "authorization_code", "mfa_code", "refresh_token", "social" }
            };

            var ssoConfigs = new List<SocialLoginCredential>
            {
                new SocialLoginCredential
                {
                    Provider = "Google",
                    Audience = "google-audience",
                    IsDisabled = false,
                    ClientId = "google-client-id",
                    ClientSecret = "google-client-secret",
                    AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth",
                    TokenUrl = "https://oauth2.googleapis.com/token",
                    GetProfileUrl = "https://www.googleapis.com/oauth2/v1/userinfo",
                    RedirectUrl = "https://example.com/callback",
                    Scope = "openid profile email"
                },
                new SocialLoginCredential
                {
                    Provider = "Facebook",
                    Audience = "fb-audience",
                    IsDisabled = false,
                    ClientId = "fb-client-id",
                    ClientSecret = "fb-client-secret",
                    AuthorizationUrl = "https://www.facebook.com/v12.0/dialog/oauth",
                    TokenUrl = "https://graph.facebook.com/v12.0/oauth/access_token",
                    GetProfileUrl = "https://graph.facebook.com/me",
                    RedirectUrl = "https://example.com/callback",
                    Scope = "email public_profile"
                }
            };

            _mockAuthenticationRepository.Setup(x => x.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(config);
            _mockAuthenticationDomainService.Setup(x => x.GetSocialLoginCredentialsAsync())
                .ReturnsAsync(ssoConfigs);

            // Act
            var actionResult = await _service.GetLoginOptionsAsync();

            // Assert
            var okResult = actionResult as OkObjectResult;
            okResult.Should().NotBeNull();
            okResult!.Value.Should().NotBeNull();

            var valueType = okResult.Value.GetType();
            var grantTypesProperty = valueType.GetProperty("AllowedGrantTypes");
            var allowedGrantTypes = grantTypesProperty!.GetValue(okResult.Value) as IEnumerable<string>;

            allowedGrantTypes.Should().NotBeNull();
            allowedGrantTypes.Should().NotContain("mfa_code");
            allowedGrantTypes.Should().NotContain("refresh_token");
            allowedGrantTypes.Should().Contain("password");
            allowedGrantTypes.Should().Contain("social");
        }

        [Fact]
        public async Task GetLoginOptionsAsync_WithSocial_Should_ReturnSsoInfo()
        {
            // Arrange
            var config = new AuthenticationConfiguration
            {
                AllowedGrantTypes = new List<string> { "password", "social" }
            };

            var ssoConfigs = new List<SocialLoginCredential>
            {
                new SocialLoginCredential
                {
                    Provider = "Google",
                    Audience = "google-audience",
                    IsDisabled = false,
                    ClientId = "google-client-id",
                    ClientSecret = "google-client-secret",
                    AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth",
                    TokenUrl = "https://oauth2.googleapis.com/token",
                    GetProfileUrl = "https://www.googleapis.com/oauth2/v1/userinfo",
                    RedirectUrl = "https://example.com/callback",
                    Scope = "openid profile email"
                },
                new SocialLoginCredential
                {
                    Provider = "Facebook",
                    Audience = "fb-audience",
                    IsDisabled = false,
                    ClientId = "fb-client-id",
                    ClientSecret = "fb-client-secret",
                    AuthorizationUrl = "https://www.facebook.com/v12.0/dialog/oauth",
                    TokenUrl = "https://graph.facebook.com/v12.0/oauth/access_token",
                    GetProfileUrl = "https://graph.facebook.com/me",
                    RedirectUrl = "https://example.com/callback",
                    Scope = "email public_profile"
                }
            };

            _mockAuthenticationRepository.Setup(x => x.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(config);
            _mockAuthenticationDomainService.Setup(x => x.GetSocialLoginCredentialsAsync())
                .ReturnsAsync(ssoConfigs);

            // Act
            var actionResult = await _service.GetLoginOptionsAsync();

            // Assert
            var okResult = actionResult as OkObjectResult;
            okResult.Should().NotBeNull();

            var valueType = okResult!.Value!.GetType();
            var ssoInfoProperty = valueType.GetProperty("SsoInfo");
            ssoInfoProperty.Should().NotBeNull();

            var ssoInfo = ssoInfoProperty!.GetValue(okResult.Value);
            ssoInfo.Should().NotBeNull();
        }

        [Fact]
        public async Task GetLoginOptionsAsync_WithoutSocial_Should_ReturnNullSsoInfo()
        {
            // Arrange
            var config = new AuthenticationConfiguration
            {
                AllowedGrantTypes = new List<string> { "password", "authorization_code" }
            };
            var ssoConfigs = new List<SocialLoginCredential>();

            _mockAuthenticationRepository.Setup(x => x.GetAuthenticationConfigurationAsync())
                .ReturnsAsync(config);
            _mockAuthenticationDomainService.Setup(x => x.GetSocialLoginCredentialsAsync())
                .ReturnsAsync(ssoConfigs);

            // Act
            var actionResult = await _service.GetLoginOptionsAsync();

            // Assert
            var okResult = actionResult as OkObjectResult;
            okResult.Should().NotBeNull();

            var valueType = okResult!.Value!.GetType();
            var ssoInfoProperty = valueType.GetProperty("SsoInfo");
            var ssoInfo = ssoInfoProperty!.GetValue(okResult.Value);
            ssoInfo.Should().BeNull();
        }
    }
}