using Api.Controllers;
using Blocks.Genesis;
using DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace XUnitTest.Controllers
{
    public class DiscoveryControllerTests : IDisposable
    {
        private readonly Mock<ICacheClient> _mockCacheClient;
        private readonly Mock<ITenants> _mockTenants;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<IDatabase> _mockDatabase;
        private readonly DiscoveryController _controller;
        private readonly X509Certificate2 _testCertificate;

        public DiscoveryControllerTests()
        {
            _mockCacheClient = new Mock<ICacheClient>();
            _mockTenants = new Mock<ITenants>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockDatabase = new Mock<IDatabase>();

            _mockCacheClient.Setup(x => x.CacheDatabase()).Returns(_mockDatabase.Object);

            _controller = new DiscoveryController(
                _mockCacheClient.Object,
                _mockTenants.Object,
                _mockConfiguration.Object);

            // Generate test certificate
            _testCertificate = GenerateTestCertificate();

            // Setup default configuration
            _mockConfiguration.Setup(x => x["OpenIdConnect:IssuerUri"])
                .Returns("https://test-idp.com");

            // Setup BlocksContext with default tenant
            SetupBlocksContext("test-tenant-id");
        }

        private void SetupBlocksContext(string tenantId)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Items["TenantId"] = tenantId;
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        private X509Certificate2 GenerateTestCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Test Certificate",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            return request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(365));
        }

        [Fact]
        public async Task GetJwks_WithProjectKey_ReturnsValidJwks()
        {
            // Arrange
            var projectKey = "custom-project-key";
            var certPassword = "test-password";
            var certData = _testCertificate.Export(X509ContentType.Pkcs12, certPassword);

            var tokenParams = new JwtTokenParameters
            {
                PublicCertificatePassword = certPassword,
                PrivateCertificatePassword = certPassword,
                IssueDate = DateTime.UtcNow
            };

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

            _mockDatabase.Setup(x => x.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString() == $"{IdpConstants.TenantTokenPublicCertificateCachePrefix}{projectKey}"),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)certData);

            _mockTenants.Setup(x => x.GetTenantTokenValidationParameter("test-tenant-id"))
                .Returns(tokenParams);

            // Act
            var result = await _controller.GetJwks(projectKey);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var jwks = okResult.Value;
            Assert.NotNull(jwks);

            var keysProperty = jwks.GetType().GetProperty("keys");
            var keys = keysProperty?.GetValue(jwks) as JsonWebKey[];
            Assert.NotNull(keys);
            Assert.Single(keys);

            var jwk = keys[0];
            Assert.Equal("RSA", jwk.Kty);
            Assert.Equal("sig", jwk.Use);
            Assert.Equal(SecurityAlgorithms.RsaSha256, jwk.Alg);
            Assert.NotEmpty(jwk.N);
            Assert.NotEmpty(jwk.E);
            Assert.NotEmpty(jwk.Kid);
        }

        [Fact]
        public async Task GetJwks_WithoutProjectKey_UsesContextTenantId()
        {
            // Arrange
            var tenantId = "test-tenant-id";
            var certPassword = "test-password";
            var certData = _testCertificate.Export(X509ContentType.Pkcs12, certPassword);

            var tokenParams = new JwtTokenParameters
            {
                PublicCertificatePassword = certPassword,
                PrivateCertificatePassword = certPassword,
                IssueDate = DateTime.UtcNow
            };

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

            _mockDatabase.Setup(x => x.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString() == $"{IdpConstants.TenantTokenPublicCertificateCachePrefix}{tenantId}"),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)certData);

            _mockTenants.Setup(x => x.GetTenantTokenValidationParameter(tenantId))
                .Returns(tokenParams);

            // Act
            var result = await _controller.GetJwks(null);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            _mockDatabase.Verify(x => x.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString() == $"{IdpConstants.TenantTokenPublicCertificateCachePrefix}{tenantId}"),
                It.IsAny<CommandFlags>()), Times.Once);
        }
        
        [Fact]
        public async Task GetJwks_WhenPkcs12LoadFails_FallsBackToLoadCertificate()
        {
            // Arrange
            var projectKey = "test-key";
            var certData = _testCertificate.Export(X509ContentType.Cert);

            var tokenParams = new JwtTokenParameters
            {
                PublicCertificatePassword = "wrong-password",
                PrivateCertificatePassword = "wrong-password",
                IssueDate = DateTime.UtcNow
            };

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

            _mockDatabase.Setup(x => x.StringGetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)certData);

            _mockTenants.Setup(x => x.GetTenantTokenValidationParameter("test-tenant-id"))
                .Returns(tokenParams);

            // Act
            var result = await _controller.GetJwks(projectKey);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetJwks_ReturnsCorrectJwkStructure()
        {
            // Arrange
            var certPassword = "test-password";
            var certData = _testCertificate.Export(X509ContentType.Pkcs12, certPassword);

            var tokenParams = new JwtTokenParameters
            {
                PublicCertificatePassword = certPassword,
                PrivateCertificatePassword = certPassword,
                IssueDate = DateTime.UtcNow
            };

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

            _mockDatabase.Setup(x => x.StringGetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)certData);

            _mockTenants.Setup(x => x.GetTenantTokenValidationParameter("test-tenant-id"))
                .Returns(tokenParams);

            // Act
            var result = await _controller.GetJwks("test-key");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var jwks = okResult.Value;

            var rsaPublicKey = _testCertificate.GetRSAPublicKey();
            var rsaParameters = rsaPublicKey.ExportParameters(false);

            var keysProperty = jwks.GetType().GetProperty("keys");
            var keys = keysProperty?.GetValue(jwks) as JsonWebKey[];
            var jwk = keys[0];

            Assert.Equal(Base64UrlEncoder.Encode(rsaParameters.Modulus), jwk.N);
            Assert.Equal(Base64UrlEncoder.Encode(rsaParameters.Exponent), jwk.E);
            Assert.Equal(Base64UrlEncoder.Encode(_testCertificate.Thumbprint), jwk.Kid);
        }

        [Fact]
        public void GetOpenIdConfiguration_WithProjectKey_ReturnsValidConfiguration()
        {
            // Arrange
            var projectKey = "custom-project-key";
            var issuerUri = "https://test-idp.com";

            // Act
            var result = _controller.GetOpenIdConfiguration(projectKey);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var config = okResult.Value;
            Assert.NotNull(config);

            var issuerProperty = config.GetType().GetProperty("issuer");
            var authEndpointProperty = config.GetType().GetProperty("authorization_endpoint");
            var tokenEndpointProperty = config.GetType().GetProperty("token_endpoint");
            var userinfoEndpointProperty = config.GetType().GetProperty("userinfo_endpoint");
            var jwksUriProperty = config.GetType().GetProperty("jwks_uri");

            Assert.Equal(issuerUri, issuerProperty?.GetValue(config));
            Assert.Equal($"{issuerUri}/Authentication/authorize?X-Blocks-Key={projectKey}", 
                authEndpointProperty?.GetValue(config));
            Assert.Equal($"{issuerUri}/Authentication/token?X-Blocks-Key={projectKey}", 
                tokenEndpointProperty?.GetValue(config));
            Assert.Equal($"{issuerUri}/Authentication/GetUserInfo?X-Blocks-Key={projectKey}", 
                userinfoEndpointProperty?.GetValue(config));
            Assert.Equal($"{issuerUri}/.well-known/jwks.json?X-Blocks-Key={projectKey}", 
                jwksUriProperty?.GetValue(config));
        }

        [Fact]
        public void GetOpenIdConfiguration_WithoutProjectKey_UsesContextTenantId()
        {
            // Arrange
            var tenantId = "test-tenant-id";
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

            // Act
            var result = _controller.GetOpenIdConfiguration(null);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var config = okResult.Value;

            var authEndpointProperty = config.GetType().GetProperty("authorization_endpoint");
            var authEndpoint = authEndpointProperty?.GetValue(config) as string;

            Assert.Contains(tenantId, authEndpoint);
        }

        [Fact]
        public void GetOpenIdConfiguration_ReturnsCorrectMetadata()
        {
            // Arrange & Act
            var result = _controller.GetOpenIdConfiguration("test-key");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var config = okResult.Value;

            var responseTypesProperty = config.GetType().GetProperty("response_types_supported");
            var subjectTypesProperty = config.GetType().GetProperty("subject_types_supported");
            var signingAlgProperty = config.GetType().GetProperty("id_token_signing_alg_values_supported");
            var scopesProperty = config.GetType().GetProperty("scopes_supported");
            var authMethodsProperty = config.GetType().GetProperty("token_endpoint_auth_methods_supported");

            var responseTypes = responseTypesProperty?.GetValue(config) as string[];
            var subjectTypes = subjectTypesProperty?.GetValue(config) as string[];
            var signingAlgs = signingAlgProperty?.GetValue(config) as string[];
            var scopes = scopesProperty?.GetValue(config) as string[];
            var authMethods = authMethodsProperty?.GetValue(config) as string[];

            Assert.Contains("code", responseTypes);
            Assert.Contains("public", subjectTypes);
            Assert.Contains("RS256", signingAlgs);
            Assert.Contains("openid", scopes);
            Assert.Contains("email", scopes);
            Assert.Contains("profile", scopes);
            Assert.Contains("client_secret_basic", authMethods);
            Assert.Contains("client_secret_post", authMethods);
            Assert.Contains("client_secret_jwt", authMethods);
            Assert.Contains("private_key_jwt", authMethods);
            Assert.Contains("none", authMethods);
        }

        [Fact]
        public void GetOpenIdConfiguration_WhenIssuerUriNotConfigured_UsesFallback()
        {
            // Arrange
            _mockConfiguration.Setup(x => x["OpenIdConnect:IssuerUri"])
                .Returns((string)null);

            // Act
            var result = _controller.GetOpenIdConfiguration("test-key");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var config = okResult.Value;

            var issuerProperty = config.GetType().GetProperty("issuer");
            var issuer = issuerProperty?.GetValue(config) as string;

            Assert.Equal("https://your-idp.com", issuer);
        }

        [Fact]
        public async Task GetJwks_WithEmptyProjectKey_UsesContextTenantId()
        {
            // Arrange
            var tenantId = "test-tenant-id";
            var certPassword = "test-password";
            var certData = _testCertificate.Export(X509ContentType.Pkcs12, certPassword);

            var tokenParams = new JwtTokenParameters
            {
                PublicCertificatePassword = certPassword,
                PrivateCertificatePassword = certPassword,
                IssueDate = DateTime.UtcNow
            };

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

            _mockDatabase.Setup(x => x.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString() == $"{IdpConstants.TenantTokenPublicCertificateCachePrefix}{tenantId}"),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)certData);

            _mockTenants.Setup(x => x.GetTenantTokenValidationParameter(tenantId))
                .Returns(tokenParams);

            // Act
            var result = await _controller.GetJwks("   ");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public void GetOpenIdConfiguration_WithEmptyProjectKey_UsesContextTenantId()
        {
            // Arrange
            var tenantId = "test-tenant-id";
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


            // Act
            var result = _controller.GetOpenIdConfiguration("   ");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var config = okResult.Value;

            var jwksUriProperty = config.GetType().GetProperty("jwks_uri");
            var jwksUri = jwksUriProperty?.GetValue(config) as string;

            Assert.Contains(tenantId, jwksUri);
        }

        public void Dispose()
        {
            _testCertificate?.Dispose();
        }
    }
}