using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.DomainService.OAuth.Services
{
    public class PasswordAuthenticationServiceTests
    {
        private readonly Mock<ILogger<PasswordAuthenticationService>> _logger = new();
        private readonly Mock<IOAuthJwtAccessTokenManager> _oAuthJwtAccessTokenManager = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICryptoService> _cryptoService = new();
        private readonly Mock<IAuthenticationRepository> _oAuthRepository = new();
        private readonly PasswordAuthenticationService _service;

        public PasswordAuthenticationServiceTests()
        {
            _service = new PasswordAuthenticationService(
                _logger.Object,
                _oAuthJwtAccessTokenManager.Object,
                _tenants.Object,
                _cryptoService.Object,
                _oAuthRepository.Object);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidUser_ReturnsInvalidResponse()
        {
            // Arrange
            var request = new TokenRequest
            {
                Username = "nonexistent@example.com",
                Password = "password123",
                OrganizationId = "org-123",
                GrantType = "password"
            };
            var authConfig = new AuthenticationConfiguration();

            _oAuthRepository
                .Setup(x => x.GetUserByUsernameAsync(request.Username, request.OrganizationId))
                .ReturnsAsync((User)null);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().NotBeNullOrEmpty();
            _oAuthRepository.Verify(x => x.GetUserByUsernameAsync(request.Username, request.OrganizationId), Times.Once);
            _cryptoService.Verify(x => x.Hash(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _oAuthJwtAccessTokenManager.Verify(
                x => x.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<AuthenticationConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo>()),
                Times.Never);
        }

        [Fact]
        public async Task AuthenticateAsync_WithValidUserAndCorrectPassword_ReturnsSuccessfulTokenResponse()
        {
            // Arrange
            var request = new TokenRequest
            {
                Username = "test@example.com",
                Password = "password123",
                OrganizationId = "org-123",
                GrantType = "password"
            };
            var authConfig = new AuthenticationConfiguration();
            var hashedPassword = "hashed-password-xyz";
            var tenant = new Tenant
            {
                TenantId = "tenant-123",
                TenantSalt = "salt-abc",
                ApplicationDomain = "example.com",
                DbConnectionString = "Server=test;Database=test;",
                JwtTokenParameters = new JwtTokenParameters()
                {
                    PrivateCertificatePassword = "test-private-cert-password",
                    IssueDate = DateTime.UtcNow
                }
            };
            var user = new User
            {
                ItemId = "user-789",
                Email = "test@example.com",
                UserName = "test@example.com",
                Password = hashedPassword,
                Active = true,
                IsVarified = true
            };
            var expectedTokenResponse = new TokenResponse
            {
                AccessToken = "access-token-123",
                ExpiresIn = 3600,
                RefreshToken = "refresh-token-456"
            };

            _oAuthRepository
                .Setup(x => x.GetUserByUsernameAsync(request.Username, request.OrganizationId))
                .ReturnsAsync(user);
            _tenants
                .Setup(x => x.GetTenantByID(It.IsAny<string>()))
                .Returns(tenant);
            _cryptoService
                .Setup(x => x.Hash(request.Password, tenant.TenantSalt))
                .Returns(hashedPassword);
            _oAuthJwtAccessTokenManager
                .Setup(x => x.ManageTokenAsync(request, authConfig, user, null))
                .ReturnsAsync(expectedTokenResponse);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.AccessToken.Should().Be("access-token-123");
            result.ExpiresIn.Should().Be(3600);
            result.RefreshToken.Should().Be("refresh-token-456");
            result.Error.Should().BeNullOrEmpty();
            _oAuthRepository.Verify(x => x.GetUserByUsernameAsync(request.Username, request.OrganizationId), Times.Once);
            _cryptoService.Verify(x => x.Hash(request.Password, tenant.TenantSalt), Times.Once);
            _oAuthJwtAccessTokenManager.Verify(x => x.ManageTokenAsync(request, authConfig, user, null), Times.Once);
        }

        [Fact]
        public void HashPassword_WithValidTenant_ReturnsHashedPassword()
        {
            // Arrange
            var password = "mySecurePassword123";
            var tenant = new Tenant
            {
                TenantId = "tenant-456",
                TenantSalt = "salt-xyz",
                ApplicationDomain = "example.com",
                DbConnectionString = "Server=test;Database=test;",
                JwtTokenParameters = new JwtTokenParameters()
                {
                    PrivateCertificatePassword = "test-private-cert-password",
                    IssueDate = DateTime.UtcNow
                }
            };
            var expectedHash = "hashed-password-result";

            _tenants
                .Setup(x => x.GetTenantByID(It.IsAny<string>()))
                .Returns(tenant);
            _cryptoService
                .Setup(x => x.Hash(password, tenant.TenantSalt))
                .Returns(expectedHash);

            // Act
            var result = _service.HashPassword(password);

            // Assert
            result.Should().Be(expectedHash);
            _tenants.Verify(x => x.GetTenantByID(It.IsAny<string>()), Times.Once);
            _cryptoService.Verify(x => x.Hash(password, tenant.TenantSalt), Times.Once);
        }

        [Fact]
        public void HashPassword_WithNullTenant_CallsHashWithNullSalt()
        {
            // Arrange
            var password = "mySecurePassword123";
            var expectedHash = "hashed-password-without-salt";

            _tenants
                .Setup(x => x.GetTenantByID(It.IsAny<string>()))
                .Returns((Tenant)null);
            _cryptoService
                .Setup(x => x.Hash(password, null))
                .Returns(expectedHash);

            // Act
            var result = _service.HashPassword(password);

            // Assert
            result.Should().Be(expectedHash);
            _cryptoService.Verify(x => x.Hash(password, null), Times.Once);
        }
    }
}