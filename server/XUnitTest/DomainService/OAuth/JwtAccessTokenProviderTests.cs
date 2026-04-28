using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using StackExchange.Redis;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace XUnitTest.DomainService.OAuth
{
    public class JwtAccessTokenProviderTests
    {
        private readonly Mock<ILogger<JwtAccessTokenProvider>> _logger;
        private readonly Mock<ICacheClient> _cacheClient;
        private readonly Mock<IDatabase> _cacheDatabase;
        private readonly Mock<ICryptoService> _cryptoService;
        private readonly Mock<ICertificateProviderFactory> _certificateProviderFactory;
        private readonly Mock<ICertificateProvider> _certificateProvider;
        private readonly JwtAccessTokenProvider _provider;

        public JwtAccessTokenProviderTests()
        {
            _logger = new Mock<ILogger<JwtAccessTokenProvider>>();
            _cacheClient = new Mock<ICacheClient>();
            _cacheDatabase = new Mock<IDatabase>();
            _cryptoService = new Mock<ICryptoService>();
            _certificateProviderFactory = new Mock<ICertificateProviderFactory>();
            _certificateProvider = new Mock<ICertificateProvider>();

            _cacheClient.Setup(x => x.CacheDatabase()).Returns(_cacheDatabase.Object);

            _provider = new JwtAccessTokenProvider(
                _logger.Object,
                _cacheClient.Object,
                _cryptoService.Object,
                _certificateProviderFactory.Object
            );
        }

        #region GetJwtAccessToken Tests

        [Fact]
        public async Task GetJwtAccessToken_WithValidData_ReturnsMappedToken()
        {
            // Arrange
            var authConfig = CreateAuthenticationConfiguration();
            var tenant = CreateTenant();
            var user = CreateUser();
            var stateInfo = new StateInfo { Provider = "test-provider", Audience = "test-audience", Nonce = "test-nonce" };
            var certificate = GenerateTestCertificate();

            _cryptoService.Setup(x => x.Hash(It.IsAny<byte[]>(), It.IsAny<bool>())).Returns("hashed-key");
            _cacheDatabase.Setup(x => x.StringGet("hashed-key", CommandFlags.None)).Returns((RedisValue)certificate);

            // Act
            var result = await _provider.GetJwtAccessToken(authConfig, tenant, user, stateInfo, "org-123");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(tenant.JwtTokenParameters.Issuer, result.Issuer);
            Assert.Contains(result.Claims, c => c.Type == "nonce" && c.Value == "test-nonce");
        }

        [Fact]
        public async Task GetJwtAccessToken_WhenCertificateIsNull_ReturnsEmptyToken()
        {
            // Arrange
            var authConfig = CreateAuthenticationConfiguration();
            var tenant = CreateTenant();
            var user = CreateUser();

            _cryptoService.Setup(x => x.Hash(It.IsAny<byte[]>(), It.IsAny<bool>())).Returns("hashed-key");
            _cacheDatabase.Setup(x => x.StringGet("hashed-key", CommandFlags.None)).Returns(RedisValue.Null);
            _certificateProviderFactory.Setup(x => x.GetProvider(It.IsAny<CertificateStorageType>())).Returns(_certificateProvider.Object);
            _certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<string>())).ReturnsAsync((byte[])null);

            // Act
            var result = await _provider.GetJwtAccessToken(authConfig, tenant, user);

            // Assert
            Assert.NotNull(result);
            Assert.Null(result.Issuer);
        }

        #endregion

        #region MapJwtAccessToken Tests

        [Fact]
        public void MapJwtAccessToken_WithAllParameters_CreatesCompleteToken()
        {
            // Arrange
            var authConfig = CreateAuthenticationConfiguration();
            var tenant = CreateTenant();
            tenant.JwtTokenParameters.Audiences = new List<string> { "aud1", "aud2" };
            var user = CreateUser();
            var certificate = GenerateTestCertificate();
            var stateInfo = new StateInfo { Provider = "test-provider", Audience = "test-audience", Nonce = "nonce-123" };

            // Act
            var result = _provider.MapJwtAccessToken(authConfig, tenant, user, certificate, stateInfo, "org-specific");

            // Assert
            Assert.Equal(authConfig.AccessTokenValidForNumberMinutes, result.AccessTokenValidForNumberMinute);
            Assert.Equal(authConfig.RefreshTokenValidForNumberMinutes, result.RefreshTokenValidForNumberMinute);
            Assert.Equal(authConfig.RememberMeRefreshTokenValidForNumberMinutes, result.RememberMeRefreshTokenValidForNumberMinute);
            Assert.Equal("aud1,aud2", result.Audience);
            Assert.NotNull(result.SigningCredentials);
        }

        #endregion

        #region AddClaims Tests

        [Fact]
        public void AddClaims_WithCompleteUser_AddsAllClaims()
        {
            // Arrange
            var claimsIdentity = new ClaimsIdentity("test");
            var tenant = CreateTenant();
            var user = CreateUser();
            var stateInfo = new StateInfo { Provider = "test-provider", Audience = "test-audience", Nonce = "nonce-value" };

            // Act
            JwtAccessTokenProvider.AddClaims(claimsIdentity, tenant, user, stateInfo, "org-specific");

            // Assert - Verify all claim types
            Assert.Contains(claimsIdentity.Claims, c => c.Type == BlocksContext.TENANT_ID_CLAIM && c.Value == tenant.TenantId);
            Assert.Contains(claimsIdentity.Claims, c => c.Type == BlocksContext.SUBJECT_CLAIM && c.Value == $"blocks|{user.ItemId}");
            Assert.Contains(claimsIdentity.Claims, c => c.Type == BlocksContext.USER_ID_CLAIM && c.Value == user.ItemId);
            Assert.Contains(claimsIdentity.Claims, c => c.Type == BlocksContext.ISSUED_AT_TIME_CLAIM && c.ValueType == ClaimValueTypes.Integer64);
            Assert.Contains(claimsIdentity.Claims, c => c.Type == BlocksContext.ORGANIZATION_ID_CLAIM && c.Value == "org-specific");
            Assert.Contains(claimsIdentity.Claims, c => c.Type == BlocksContext.EMAIL_CLAIM && c.Value == user.Email);
            Assert.Contains(claimsIdentity.Claims, c => c.Type == BlocksContext.USER_NAME_CLAIM && c.Value == user.UserName);
            Assert.Contains(claimsIdentity.Claims, c => c.Type == BlocksContext.DISPLAY_NAME_CLAIM);
            Assert.Contains(claimsIdentity.Claims, c => c.Type == BlocksContext.PHONE_NUMBER_CLAIM);
            Assert.Contains(claimsIdentity.Claims, c => c.Type == "nonce" && c.Value == "nonce-value");
            Assert.Equal(1, claimsIdentity.Claims.Count(c => c.Type == BlocksContext.ROLES_CLAIM));
            Assert.Equal(1, claimsIdentity.Claims.Count(c => c.Type == BlocksContext.PERMISSION_CLAIM));
        }

        [Theory]
        [InlineData(null, null, "")]
        [InlineData("John", null, "John")]
        [InlineData(null, "Doe", "Doe")]
        [InlineData("John", "Doe", "John Doe")]
        public void AddClaims_WithDifferentNames_CreatesCorrectDisplayName(string firstName, string lastName, string expected)
        {
            // Arrange
            var claimsIdentity = new ClaimsIdentity("test");
            var tenant = CreateTenant();
            var user = CreateUser();
            user.FirstName = firstName;
            user.LastName = lastName;

            // Act
            JwtAccessTokenProvider.AddClaims(claimsIdentity, tenant, user);

            // Assert
            var displayName = claimsIdentity.Claims.First(c => c.Type == BlocksContext.DISPLAY_NAME_CLAIM).Value;
            Assert.Equal(expected, displayName);
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void AddClaims_WithNullOrEmptyNonce_DoesNotAddNonceClaim(string nonce, string ignored)
        {
            // Arrange
            var claimsIdentity = new ClaimsIdentity("test");
            var tenant = CreateTenant();
            var user = CreateUser();
            var stateInfo = string.IsNullOrEmpty(nonce) ? null : new StateInfo { Provider = "test-provider", Audience = "test-audience", Nonce = nonce };

            // Act
            JwtAccessTokenProvider.AddClaims(claimsIdentity, tenant, user, stateInfo);

            // Assert
            Assert.DoesNotContain(claimsIdentity.Claims, c => c.Type == "nonce");
        }
        
        [Fact]
        public void AddClaims_WithNullPhoneNumber_AddsEmptyPhoneNumberClaim()
        {
            // Arrange
            var claimsIdentity = new ClaimsIdentity("test");
            var tenant = CreateTenant();
            var user = CreateUser();
            user.PhoneNumber = null;

            // Act
            JwtAccessTokenProvider.AddClaims(claimsIdentity, tenant, user);

            // Assert
            var phoneClaim = claimsIdentity.Claims.First(c => c.Type == BlocksContext.PHONE_NUMBER_CLAIM);
            Assert.Equal(string.Empty, phoneClaim.Value);
        }

        [Fact]
        public void AddClaims_WithEmptyMemberships_AddsNoRolesOrPermissions()
        {
            // Arrange
            var claimsIdentity = new ClaimsIdentity("test");
            var tenant = CreateTenant();
            var user = CreateUser();
            user.Memberships = new List<OrganizationMembership>();

            // Act
            JwtAccessTokenProvider.AddClaims(claimsIdentity, tenant, user);

            // Assert
            Assert.Empty(claimsIdentity.Claims.Where(c => c.Type == BlocksContext.ROLES_CLAIM));
            Assert.Empty(claimsIdentity.Claims.Where(c => c.Type == BlocksContext.PERMISSION_CLAIM));
        }

        #endregion

        #region GetOrRetrieveCertAsync Tests
        
        [Fact]
        public async Task GetOrRetrieveCertAsync_WhenEmptyCertificate_DoesNotCache()
        {
            // Arrange
            var tenant = CreateTenant();
            _cryptoService.Setup(x => x.Hash(It.IsAny<byte[]>(), It.IsAny<bool>())).Returns("empty-key");
            _cacheDatabase.Setup(x => x.StringGet("empty-key", CommandFlags.None)).Returns(RedisValue.Null);
            _certificateProviderFactory.Setup(x => x.GetProvider(It.IsAny<CertificateStorageType>())).Returns(_certificateProvider.Object);
            _certificateProvider.Setup(x => x.GetCertificateAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<byte>());

            // Act
            var result = await _provider.GetOrRetrieveCertAsync(tenant);

            // Assert
            Assert.Empty(result);
            _cacheDatabase.Verify(x => x.StringSet(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Never);
        }
        
        #endregion

        #region MakeSigningCredentials Tests

        [Fact]
        public void MakeSigningCredentials_WithValidCertificate_ReturnsSigningCredentials()
        {
            // Arrange
            var certificate = GenerateTestCertificate();

            // Act
            var result = JwtAccessTokenProvider.MakeSigningCredentials(certificate, "test-password");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(SecurityAlgorithms.RsaSha256, result.Algorithm);
            Assert.Equal(SecurityAlgorithms.Sha256Digest, result.Digest);
            Assert.IsType<RsaSecurityKey>(result.Key);
        }
        
        [Fact]
        public void MakeSigningCredentials_WithInvalidData_ThrowsInvalidOperationException()
        {
            // Arrange
            var invalidData = Encoding.UTF8.GetBytes("not-a-certificate");

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                JwtAccessTokenProvider.MakeSigningCredentials(invalidData, "password"));
            
            Assert.Contains("Failed to load X509 certificate", exception.Message);
        }

        [Fact]
        public void MakeSigningCredentials_WithoutPrivateKey_ThrowsCryptographicException()
        {
            // Arrange
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=No Private Key", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            var certWithoutPrivateKey = cert.Export(X509ContentType.Cert);

            // Act & Assert
            var exception = Assert.Throws<CryptographicException>(() =>
                JwtAccessTokenProvider.MakeSigningCredentials(certWithoutPrivateKey, ""));
            
            Assert.Contains("Invalid private key", exception.Message);
        }

        #endregion

        #region Helper Methods

        private AuthenticationConfiguration CreateAuthenticationConfiguration() => new()
        {
            AccessTokenValidForNumberMinutes = 15,
            RefreshTokenValidForNumberMinutes = 1440,
            RememberMeRefreshTokenValidForNumberMinutes = 43200
        };

        private Tenant CreateTenant() => new()
        {
            TenantId = "8656D85F-C3E0-48AA-9505-654505096AEC",
            ItemId = "item-456",
            ApplicationDomain = "test.example.com",
            DbConnectionString = "test-connection-string",
            JwtTokenParameters = new JwtTokenParameters
            {
                Issuer = "https://test-issuer.com",
                Audiences = new List<string> { "test-audience" },
                PrivateCertificatePassword = "test-password",
                CertificateStorageType = CertificateStorageType.Azure,
                CertificateValidForNumberOfDays = 365,
                IssueDate = DateTime.UtcNow.AddDays(-30)
            }
        };

        private User CreateUser() => new()
        {
            ItemId = "user-789",
            Email = "test@example.com",
            UserName = "testuser",
            FirstName = "John",
            LastName = "Doe",
            PhoneNumber = "+1234567890",
            Memberships = new List<OrganizationMembership>
            {
                new() { OrganizationId = "default", Roles = new List<string> { "admin", "user" }, Permissions = new List<string> { "read", "write" } },
                new() { OrganizationId = "org-specific", Roles = new List<string> { "manager" }, Permissions = new List<string> { "delete" } }
            }
        };

        private byte[] GenerateTestCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=Test Certificate", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return certificate.Export(X509ContentType.Pfx, "test-password");
        }

        #endregion
    }
}