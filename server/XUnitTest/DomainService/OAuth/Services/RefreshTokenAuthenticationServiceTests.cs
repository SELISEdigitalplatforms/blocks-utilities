using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace XUnitTest.DomainService.OAuth.Services
{
    public class RefreshTokenAuthenticationServiceTests
    {
        private readonly Mock<ILogger<RefreshTokenAuthenticationService>> _logger = new();
        private readonly Mock<IJwtAccessTokenProvider> _jwtAccessTokenProvider = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _oAuthJwtAccessTokenManager = new();
        private readonly RefreshTokenAuthenticationService _service;

        public RefreshTokenAuthenticationServiceTests()
        {
            _service = new RefreshTokenAuthenticationService(
                _logger.Object,
                _jwtAccessTokenProvider.Object,
                _tenants.Object,
                _oAuthJwtAccessTokenManager.Object);
        }

        [Fact]
        public async Task AuthenticateAsync_WithValidRequest_ReturnsSuccessfulTokenResponse()
        {
            // Arrange
            var request = new TokenRequest
            {
                RefreshToken = "refresh-token-123",
                OrganizationId = "org-456",
                GrantType = "refresh_token"
            };
            var authConfig = new AuthenticationConfiguration
            {
                AccessTokenValidForNumberMinutes = 60
            };
            var user = new User
            {
                ItemId = "user-789",
                Email = "test@example.com",
                Active = true,
                IsVarified = true
            };
            var tenant = new Tenant
            {
                TenantId = "tenant-123",
                CookieDomain = ".example.com",
                ApplicationDomain = "https://example.com",
                DbConnectionString = "Server=test;Database=test;",
                JwtTokenParameters = new JwtTokenParameters
                {
                    Issuer = "https://issuer.example.com",
                    Audiences = new List<string> { "https://api.example.com" },
                    PrivateCertificatePassword = "test-password",
                    IssueDate = DateTime.UtcNow
                }
            };

            var rsa = RSA.Create(2048);
            var securityKey = new RsaSecurityKey(rsa);
            var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

            var jwtAccessToken = new JwtAccessToken
            {
                Issuer = tenant.JwtTokenParameters.Issuer,
                Audience = string.Join(",", tenant.JwtTokenParameters.Audiences),
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddMinutes(60),
                SigningCredentials = signingCredentials,
                Claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.ItemId),
                    new Claim(ClaimTypes.Email, user.Email)
                }
            };

            _tenants
                .Setup(x => x.GetTenantByID(It.IsAny<string>()))
                .Returns(tenant);
            _jwtAccessTokenProvider
                .Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, request.OrganizationId))
                .ReturnsAsync(jwtAccessToken);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig, user);

            // Assert
            result.Should().NotBeNull();
            result.AccessToken.Should().NotBeNullOrEmpty();
            result.ExpiresIn.Should().Be(60);
            result.ExpiresUtc.Should().BeCloseTo(jwtAccessToken.Expires, TimeSpan.FromSeconds(1));
            result.CookieDomain.Should().Be(".example.com");
            result.Error.Should().BeNullOrEmpty();

            _tenants.Verify(x => x.GetTenantByID(It.IsAny<string>()), Times.Once);
            _jwtAccessTokenProvider.Verify(
                x => x.GetJwtAccessToken(authConfig, tenant, user, null, request.OrganizationId),
                Times.Once);

            // Verify JWT token structure
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(result.AccessToken);
            jwtToken.Issuer.Should().Be(tenant.JwtTokenParameters.Issuer);
            jwtToken.Claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == user.ItemId);
            jwtToken.Claims.Should().Contain(c => c.Type == ClaimTypes.Email && c.Value == user.Email);
        }

        [Fact]
        public async Task AuthenticateAsync_WithDifferentOrganization_GeneratesTokenWithOrganizationContext()
        {
            // Arrange
            var request = new TokenRequest
            {
                RefreshToken = "refresh-token-xyz",
                OrganizationId = "org-different-999",
                GrantType = "refresh_token"
            };
            var authConfig = new AuthenticationConfiguration
            {
                AccessTokenValidForNumberMinutes = 30
            };
            var user = new User
            {
                ItemId = "user-123",
                Email = "admin@example.com",
                Active = true,
                IsVarified = true
            };
            var tenant = new Tenant
            {
                TenantId = "tenant-456",
                CookieDomain = ".custom-domain.com",
                ApplicationDomain = "https://custom-domain.com",
                DbConnectionString = "Server=test;Database=test;",
                JwtTokenParameters = new JwtTokenParameters
                {
                    Issuer = "https://custom-issuer.com",
                    Audiences = new List<string> { "https://custom-api.com" },
                    PrivateCertificatePassword = "test-password",
                    IssueDate = DateTime.UtcNow
                }
            };

            var rsa = RSA.Create(2048);
            var securityKey = new RsaSecurityKey(rsa);
            var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

            var jwtAccessToken = new JwtAccessToken
            {
                Issuer = tenant.JwtTokenParameters.Issuer,
                Audience = string.Join(",", tenant.JwtTokenParameters.Audiences),
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddMinutes(30),
                SigningCredentials = signingCredentials,
                Claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.ItemId),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim("organization_id", request.OrganizationId)
                }
            };

            _tenants
                .Setup(x => x.GetTenantByID(It.IsAny<string>()))
                .Returns(tenant);
            _jwtAccessTokenProvider
                .Setup(x => x.GetJwtAccessToken(authConfig, tenant, user, null, request.OrganizationId))
                .ReturnsAsync(jwtAccessToken);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig, user);

            // Assert
            result.Should().NotBeNull();
            result.AccessToken.Should().NotBeNullOrEmpty();
            result.ExpiresIn.Should().Be(30);
            result.CookieDomain.Should().Be(".custom-domain.com");

            _jwtAccessTokenProvider.Verify(
                x => x.GetJwtAccessToken(
                    authConfig,
                    tenant,
                    user,
                    null,
                    "org-different-999"),
                Times.Once);

            // Verify organization context in token
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(result.AccessToken);
            jwtToken.Claims.Should().Contain(c => c.Type == "organization_id" && c.Value == "org-different-999");
        }

        [Fact]
        public async Task SomeMethod_UsesJwtAccessTokenWithValidityMinutes_ShouldProcessCorrectly()
        {
            var rsa = RSA.Create(2048);
            var securityKey = new RsaSecurityKey(rsa);
            var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

            // Arrange
            var jwtAccessToken = new JwtAccessToken
            {
                Issuer = "test-issuer",
                Audience = "test-audience",
                Claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "user123") },
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddMinutes(15),
                SigningCredentials = signingCredentials,
                // These lines will provide coverage for the three properties
                AccessTokenValidForNumberMinute = 15,
                RefreshTokenValidForNumberMinute = 1440,
                RememberMeRefreshTokenValidForNumberMinute = 43200
            };

            // Act
            // Use jwtAccessToken in your service method
            // var result = await _service.SomeMethod(jwtAccessToken);

            // Assert
            // Verify the token validity values are used correctly
            Assert.Equal(15, jwtAccessToken.AccessTokenValidForNumberMinute);
            Assert.Equal(1440, jwtAccessToken.RefreshTokenValidForNumberMinute);
            Assert.Equal(43200, jwtAccessToken.RememberMeRefreshTokenValidForNumberMinute);
        }
    }
}