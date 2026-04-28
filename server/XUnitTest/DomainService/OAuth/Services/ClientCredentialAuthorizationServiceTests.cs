using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.Services;
using DomainService.Services;
using FluentAssertions;
using Moq;
using StackExchange.Redis;
using System.Security.Claims;

namespace XUnitTest.DomainService.OAuth.Services
{
    public class ClientCredentialAuthorizationServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _authenticationRepository = new();
        private readonly Mock<ICertificateProviderFactory> _certificateProviderFactory = new();
        private readonly Mock<ICryptoService> _cryptoService = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly ClientCredentialAuthorizationService _service;

        public ClientCredentialAuthorizationServiceTests()
        {
            _service = new ClientCredentialAuthorizationService(
                _authenticationRepository.Object,
                _certificateProviderFactory.Object,
                _cryptoService.Object,
                _cacheClient.Object,
                _tenants.Object);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidClient_ReturnsInvalidClientError()
        {
            // Arrange
            var request = new TokenRequest
            {
                ClientId = "invalid-client-id",
                ClientSecret = "secret-123",
                GrantType = "client_credentials"
            };
            var authConfig = new AuthenticationConfiguration();

            _authenticationRepository
                .Setup(x => x.GetClientCredentialByIdAsync(request.ClientId))
                .ReturnsAsync((ClientCredential)null);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().Be("invalid_client");
            result.ErrorDescription.Should().Be("No client found");
            _authenticationRepository.Verify(x => x.GetClientCredentialByIdAsync(request.ClientId), Times.Once);
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidClientSecret_ReturnsInvalidClientError()
        {
            // Arrange
            var request = new TokenRequest
            {
                ClientId = "valid-client-id",
                ClientSecret = "wrong-secret",
                GrantType = "client_credentials"
            };
            var authConfig = new AuthenticationConfiguration();
            var client = new ClientCredential
            {
                ItemId = "valid-client-id",
                ClientSecret = "correct-secret",
                IsActive = true,
                Roles = new List<string> { "admin" }
            };

            _authenticationRepository
                .Setup(x => x.GetClientCredentialByIdAsync(request.ClientId))
                .ReturnsAsync(client);

            // Act
            var result = await _service.AuthenticateAsync(request, authConfig);

            // Assert
            result.Should().NotBeNull();
            result.Error.Should().Be("invalid_client");
            result.ErrorDescription.Should().Be("Client secret not match");
        }

        [Fact]
        public async Task RetrievePrivateCertAsync_WithCachedCertificate_ReturnsCachedValue()
        {
            // Arrange
            var tenant = new Tenant
            {
                TenantId = "tenant-123",
                ItemId = "item-456",
                ApplicationDomain = "test.example.com",
                DbConnectionString = "mongodb://localhost:27017/test",
                JwtTokenParameters = new JwtTokenParameters
                {
                    CertificateStorageType = CertificateStorageType.Azure,
                    CertificateValidForNumberOfDays = 365,
                    IssueDate = DateTime.UtcNow.AddDays(-10),
                    PrivateCertificatePassword = "test-password"
                }
            };
            var hashedKey = "hashed-key-123";
            var cachedCertificate = new byte[] { 1, 2, 3, 4, 5 };
            var mockDatabase = new Mock<IDatabase>();

            _cryptoService
                .Setup(x => x.Hash(It.IsAny<byte[]>(), false))
                .Returns(hashedKey);
            _cacheClient
                .Setup(x => x.CacheDatabase())
                .Returns(mockDatabase.Object);
            mockDatabase
                .Setup(x => x.StringGet(hashedKey, It.IsAny<CommandFlags>()))
                .Returns((RedisValue)cachedCertificate);

            // Act
            var result = await _service.RetrievePrivateCertAsync(tenant);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEquivalentTo(cachedCertificate);
            _certificateProviderFactory.Verify(
                x => x.GetProvider(It.IsAny<CertificateStorageType>()),
                Times.Never);
        }

        [Fact]
        public async Task RetrievePrivateCertAsync_WithoutCachedCertificate_RetrievesFromProvider()
        {
            // Arrange
            var tenant = new Tenant
            {
                TenantId = "tenant-123",
                ItemId = "item-456",
                ApplicationDomain = "test.example.com",
                DbConnectionString = "mongodb://localhost:27017/test",
                JwtTokenParameters = new JwtTokenParameters
                {
                    CertificateStorageType = CertificateStorageType.Mongodb,
                    CertificateValidForNumberOfDays = 365,
                    IssueDate = DateTime.UtcNow.AddDays(-10),
                    PrivateCertificatePassword = "test-password"
                }
            };
            var hashedKey = "hashed-key-123";
            var certificate = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var mockDatabase = new Mock<IDatabase>();
            var mockProvider = new Mock<ICertificateProvider>();

            _cryptoService
                .Setup(x => x.Hash(It.IsAny<byte[]>(), false))
                .Returns(hashedKey);
            _cacheClient
                .Setup(x => x.CacheDatabase())
                .Returns(mockDatabase.Object);
            mockDatabase
                .Setup(x => x.StringGet(hashedKey, It.IsAny<CommandFlags>()))
                .Returns(RedisValue.Null);
            _certificateProviderFactory
                .Setup(x => x.GetProvider(tenant.JwtTokenParameters.CertificateStorageType))
                .Returns(mockProvider.Object);
            mockProvider
                .Setup(x => x.GetCertificateAsync(hashedKey))
                .ReturnsAsync(certificate);
            mockDatabase
                .Setup(x => x.StringSet(hashedKey, It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .Returns(true);

            // Act
            var result = await _service.RetrievePrivateCertAsync(tenant);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEquivalentTo(certificate);
            _certificateProviderFactory.Verify(
                x => x.GetProvider(tenant.JwtTokenParameters.CertificateStorageType),
                Times.Once);
            mockProvider.Verify(x => x.GetCertificateAsync(hashedKey), Times.Once);
        }

        [Fact]
        public void AddClaims_WithClientCredential_AddsAllRequiredClaims()
        {
            // Arrange
            var claimsIdentity = new ClaimsIdentity("test-auth");
            var tenant = new Tenant
            {
                TenantId = "tenant-123",
                ItemId = "tenant-item-456",
                ApplicationDomain = "test.example.com",
                DbConnectionString = "mongodb://localhost:27017/test",
                JwtTokenParameters = new JwtTokenParameters()
                {
                    PrivateCertificatePassword = "test-password",
                    IssueDate = DateTime.UtcNow
                }
            };
            var client = new ClientCredential
            {
                ItemId = "client-789",
                Roles = new List<string> { "admin", "user", "editor" }
            };

            // Act
            ClientCredentialAuthorizationService.AddClaims(claimsIdentity, tenant, client);

            // Assert
            var claims = claimsIdentity.Claims.ToList();
            claims.Should().Contain(c => c.Type == BlocksContext.TENANT_ID_CLAIM && c.Value == "tenant-123");
            claims.Should().Contain(c => c.Type == BlocksContext.SUBJECT_CLAIM && c.Value == "blocks|client-789");
            claims.Should().Contain(c => c.Type == "client_id" && c.Value == "client-789");
            claims.Should().Contain(c => c.Type == BlocksContext.ISSUED_AT_TIME_CLAIM);
            claims.Should().Contain(c => c.Type == BlocksContext.ROLES_CLAIM && c.Value == "admin");
            claims.Should().Contain(c => c.Type == BlocksContext.ROLES_CLAIM && c.Value == "user");
            claims.Should().Contain(c => c.Type == BlocksContext.ROLES_CLAIM && c.Value == "editor");
            claims.Count.Should().Be(7); // tenant_id, subject, client_id, issued_at, 3 roles
        }

        [Fact]
        public void AddClaims_WithClientWithoutRoles_AddsBaseClaimsOnly()
        {
            // Arrange
            var claimsIdentity = new ClaimsIdentity("test-auth");
            var tenant = new Tenant
            {
                TenantId = "tenant-123",
                ItemId = "tenant-item-456",
                ApplicationDomain = "test.example.com",
                DbConnectionString = "mongodb://localhost:27017/test",
                JwtTokenParameters = new JwtTokenParameters()
                {
                    PrivateCertificatePassword = "test-password",
                    IssueDate = DateTime.UtcNow
                }
            };
            var client = new ClientCredential
            {
                ItemId = "client-789",
                Roles = new List<string>()
            };

            // Act
            ClientCredentialAuthorizationService.AddClaims(claimsIdentity, tenant, client);

            // Assert
            var claims = claimsIdentity.Claims.ToList();
            claims.Should().Contain(c => c.Type == BlocksContext.TENANT_ID_CLAIM && c.Value == "tenant-123");
            claims.Should().Contain(c => c.Type == BlocksContext.SUBJECT_CLAIM && c.Value == "blocks|client-789");
            claims.Should().Contain(c => c.Type == "client_id" && c.Value == "client-789");
            claims.Should().Contain(c => c.Type == BlocksContext.ISSUED_AT_TIME_CLAIM);
            claims.Should().NotContain(c => c.Type == BlocksContext.ROLES_CLAIM);
            claims.Count.Should().Be(4);
        }
    }
}