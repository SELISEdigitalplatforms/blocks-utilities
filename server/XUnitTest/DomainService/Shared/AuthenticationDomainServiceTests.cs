using Blocks.Genesis;
using DomainService.Entities;
using DomainService.RequestModel;
using DomainService.Services;
using DomainService.Shared;
using DomainService.Shared.RequestModel;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Net;

namespace XUnitTest.DomainService.Shared
{
    public class AuthenticationDomainServiceTests : IDisposable
    {
        private readonly Mock<IMessageClient> _messageClient;
        private readonly Mock<IAuthenticationRepository> _authenticationRepository;
        private readonly Mock<IConfiguration> _configuration;
        private readonly Mock<IUserRepository> _userRepository;
        private readonly Mock<IValidator<SaveSsoCredentialRequest>> _validator;
        private readonly Mock<ITenants> _tenants;
        private readonly AuthenticationDomainService _service;
        private readonly BlocksContext _context;

        public AuthenticationDomainServiceTests()
        {
            _messageClient = new Mock<IMessageClient>();
            _authenticationRepository = new Mock<IAuthenticationRepository>();
            _configuration = new Mock<IConfiguration>();
            _userRepository = new Mock<IUserRepository>();
            _validator = new Mock<IValidator<SaveSsoCredentialRequest>>();
            _tenants = new Mock<ITenants>();

            _service = new AuthenticationDomainService(
                _messageClient.Object,
                _authenticationRepository.Object,
                _configuration.Object,
                _userRepository.Object,
                _validator.Object,
                _tenants.Object
            );

            _context = BlocksContext.Create(
                tenantId: "tenant-123",
                roles: null,
                userId: "user-123",
                isAuthenticated: true,
                requestUri: null,
                organizationId: null,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: null,
                permissions: null,
                userName: null,
                phoneNumber: null,
                displayName: null,
                oauthToken: null,
                refreshToken: null,
                actualTentId: null
            );
            BlocksContext.SetContext(_context);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
        }

        [Theory]
        [InlineData("192.168.1.1", "192.168.1.1")]
        [InlineData("10.0.0.1, 172.16.0.1", "10.0.0.1")]
        public void GetVisitorsIpAddresses_ReturnsCorrectIpAddresses(string headerValue, string expectedFirstIp)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["X-Forwarded-For"] = headerValue;

            var result = _service.GetVisitorsIpAddresses(context);

            Assert.Contains(expectedFirstIp, result);
        }

        [Fact]
        public void GetVisitorsIpAddresses_WithoutHeader_ReturnsRemoteIp()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

            var result = _service.GetVisitorsIpAddresses(context);

            Assert.Contains("127.0.0.1", result);
        }

        [Theory]
        [InlineData("Origin", "https://example.com", "example.com")]
        [InlineData("Referer", "https://test.com/path", "test.com")]
        public void GetRequestOriginHostName_ReturnsCorrectHostName(string headerName, string headerValue, string expectedHost)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers[headerName] = headerValue;

            var result = _service.GetRequestOriginHostName(context);

            Assert.Equal(expectedHost, result);
        }

        [Fact]
        public void GetRequestOriginHostName_WithoutHeaders_ReturnsEmpty()
        {
            var context = new DefaultHttpContext();

            var result = _service.GetRequestOriginHostName(context);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public async Task SendToQueueAsync_SendsMessageToQueue()
        {
            var payload = new { Data = "test" };

            await _service.SendToQueueAsync("test-queue", payload);

            _messageClient.Verify(x => x.SendToConsumerAsync(It.Is<ConsumerMessage<object>>(
                m => m.ConsumerName == "test-queue" && m.Payload == payload)), Times.Never);
        }

        [Fact]
        public async Task SendToTopicAsync_SendsMessageToTopic()
        {
            var payload = new { Data = "test" };

            await _service.SendToTopicAsync("test-topic", payload);

            _messageClient.Verify(x => x.SendToMassConsumerAsync(It.Is<ConsumerMessage<object>>(
                m => m.ConsumerName == "test-topic" && m.Payload == payload)), Times.Never);
        }

        [Fact]
        public void GetDeviceInfo_WithValidUserAgent_ReturnsDeviceInfo()
        {
            var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

            var result = _service.GetDeviceInfo(userAgent);

            Assert.NotNull(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GetDeviceInfo_WithInvalidUserAgent_ReturnsNull(string userAgent)
        {
            var result = _service.GetDeviceInfo(userAgent);

            Assert.Null(result);
        }

        [Fact]
        public async Task SaveSocialLoginCredentialAsync_WithValidData_ReturnsSuccess()
        {
            var request = new SaveSsoCredentialRequest { Provider = "Google", ClientId = "client-123" };
            var validationResult = new ValidationResult();
            
            _validator.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(validationResult);
            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByIdAsync(It.IsAny<string>())).ReturnsAsync((SocialLoginCredential)null);
            _authenticationRepository.Setup(x => x.SaveSocialLoginCredentialAsync(It.IsAny<SocialLoginCredential>())).ReturnsAsync(true);
            _configuration.Setup(x => x[It.IsAny<string>()]).Returns("");

            var result = await _service.SaveSocialLoginCredentialAsync(request);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.ItemId);
        }

        [Fact]
        public async Task SaveSocialLoginCredentialAsync_WithInvalidData_ReturnsErrors()
        {
            var request = new SaveSsoCredentialRequest();
            var validationResult = new ValidationResult(new[] { new ValidationFailure("Provider", "Provider is required") });
            
            _validator.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(validationResult);

            var result = await _service.SaveSocialLoginCredentialAsync(request);

            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public async Task SaveSocialLoginCredentialAsync_WithExistingCredential_UpdatesCredential()
        {
            var request = new SaveSsoCredentialRequest { ItemId = "existing-id", Provider = "Google" };
            var existingCredential = new SocialLoginCredential 
            { 
                ItemId = "existing-id",
                Provider = "Google",
                Audience = "test-audience",
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret",
                AuthorizationUrl = "https://test-auth-url.com",
                TokenUrl = "https://test-token-url.com",
                GetProfileUrl = "https://test-profile-url.com",
                RedirectUrl = "https://test-redirect-url.com",
                Scope = "test-scope"
            };
            var validationResult = new ValidationResult();
            
            _validator.Setup(x => x.ValidateAsync(request, default)).ReturnsAsync(validationResult);
            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByIdAsync("existing-id")).ReturnsAsync(existingCredential);
            _authenticationRepository.Setup(x => x.SaveSocialLoginCredentialAsync(It.IsAny<SocialLoginCredential>())).ReturnsAsync(true);
            _configuration.Setup(x => x[It.IsAny<string>()]).Returns("");

            var result = await _service.SaveSocialLoginCredentialAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("existing-id", result.ItemId);
        }

        [Fact]
        public async Task DeleteSocialLoginCredentialAsync_DeletesCredential()
        {
            _authenticationRepository.Setup(x => x.DeleteSocialLoginCredentialAsync("test-id")).ReturnsAsync(true);

            var result = await _service.DeleteSocialLoginCredentialAsync("test-id");

            Assert.True(result.IsSuccess);
            _authenticationRepository.Verify(x => x.DeleteSocialLoginCredentialAsync("test-id"), Times.Once);
        }

        [Fact]
        public async Task GetSsoCredentialAsync_ReturnsCredentialWithRolesAndPermissions()
        {
            var existingCredential = new SocialLoginCredential
            {
                ItemId = "existing-id",
                Provider = "Google",
                Audience = "test-audience",
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret",
                AuthorizationUrl = "https://test-auth-url.com",
                TokenUrl = "https://test-token-url.com",
                GetProfileUrl = "https://test-profile-url.com",
                RedirectUrl = "https://test-redirect-url.com",
                Scope = "test-scope"
            };
            var roles = new List<GetUserRole> { new GetUserRole { ItemId = "role1" } };
            var permissions = new List<GetUserPermission> { new GetUserPermission { ItemId = "perm1" } };

            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialByIdAsync("test-id")).ReturnsAsync(existingCredential);
            _userRepository.Setup(x => x.GetRolesBySlugsAsync(existingCredential.InitialRoles)).ReturnsAsync(roles);
            _userRepository.Setup(x => x.GetPermissionsByResourcesAsync(existingCredential.InitialPermissions)).ReturnsAsync(permissions);

            var result = await _service.GetSsoCredentialAsync("test-id");

            Assert.NotNull(result);
            Assert.Equal(roles, result.UserRoles);
            Assert.Equal(permissions, result.UserPermissions);
        }

        [Fact]
        public async Task SaveOIDCClientAsync_WithNewClient_CreatesClient()
        {
            var request = new SaveOIDCClientRequest { Audience = "test-audience" };

            _authenticationRepository.Setup(x => x.GetOIDCClientCredentialAsync(It.IsAny<string>())).ReturnsAsync((OIDCClientCredential)null);
            _authenticationRepository.Setup(x => x.SaveOIDCClientCredentialAsync(It.IsAny<OIDCClientCredential>())).Returns(Task.CompletedTask);

            var result = await _service.SaveOIDCClientAsync(request);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.ItemId);
        }

        [Fact]
        public async Task SaveOIDCClientAsync_WithExistingClient_UpdatesClient()
        {
            var request = new SaveOIDCClientRequest { ItemId = "existing-id", Audience = "test-audience" };
            var existingClient = new OIDCClientCredential { ItemId = "existing-id" };

            _authenticationRepository.Setup(x => x.GetOIDCClientCredentialAsync("existing-id")).ReturnsAsync(existingClient);
            _authenticationRepository.Setup(x => x.SaveOIDCClientCredentialAsync(It.IsAny<OIDCClientCredential>())).Returns(Task.CompletedTask);

            var result = await _service.SaveOIDCClientAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("existing-id", result.ItemId);
        }

        [Fact]
        public async Task GetOIDCClientAsyncAsync_ReturnsClient()
        {
            var client = new OIDCClientCredential { ItemId = "test-id" };

            _authenticationRepository.Setup(x => x.GetOIDCCredentialByIdAsync("test-id")).ReturnsAsync(client);

            var result = await _service.GetOIDCClientAsyncAsync("test-id");

            Assert.True(result.IsSuccess);
            Assert.Equal(client, result.oIDCClientCredential);
        }

        [Fact]
        public async Task GetOIDCClientsAsyncAsync_ReturnsClients()
        {
            var clients = new List<OIDCClientCredential> { new OIDCClientCredential { ItemId = "test-id" } };

            _authenticationRepository.Setup(x => x.GetOIDCCredentialsByTenantAsync()).ReturnsAsync(clients);

            var result = await _service.GetOIDCClientsAsyncAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(clients, result.oIDCClientCredentials);
        }

        [Fact]
        public async Task GetOIDCClientsAsyncAsync_WithNullResult_ReturnsEmptyList()
        {
            _authenticationRepository.Setup(x => x.GetOIDCCredentialsByTenantAsync()).ReturnsAsync((List<OIDCClientCredential>)null);

            var result = await _service.GetOIDCClientsAsyncAsync();

            Assert.True(result.IsSuccess);
            Assert.Empty(result.oIDCClientCredentials);
        }

        [Fact]
        public async Task DeleteOIDCClientAsyncAsync_DeletesClient()
        {
            var request = new DeleteOIDCClientRequest { ItemId = "test-id" };

            _authenticationRepository.Setup(x => x.DeleteOidcCliantAsync(request)).Returns(Task.CompletedTask);

            var result = await _service.DeleteOIDCClientAsyncAsync(request);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task GetSocialLoginCredentialsAsync_ReturnsCredentials()
        {
            var credentials = new List<SocialLoginCredential>
            {
                new SocialLoginCredential
                {
                    ItemId = "test-id",
                    Provider = "TestProvider",
                    Audience = "test-audience",
                    ClientId = "test-client-id",
                    ClientSecret = "test-client-secret",
                    AuthorizationUrl = "https://test-auth-url.com",
                    TokenUrl = "https://test-token-url.com",
                    GetProfileUrl = "https://test-profile-url.com",
                    RedirectUrl = "https://test-redirect-url.com",
                    Scope = "test-scope"
                }
            };
            _authenticationRepository.Setup(x => x.GetSocialLoginCredentialsAsync()).ReturnsAsync(credentials);

            var result = await _service.GetSocialLoginCredentialsAsync();

            Assert.Equal(credentials, result);
        }

        [Fact]
        public async Task UpdateSsoCredentialStatusAsync_WithValidId_UpdatesStatus()
        {
            var request = new UpdateSsoCredentialStatusRequest { ItemId = "test-id", IsEnabled = false };

            _authenticationRepository.Setup(x => x.UpdatePartialAsync<SocialLoginCredential>(
                "test-id", It.IsAny<Dictionary<string, object>>(), "SocialLoginCredentials")).Returns(Task.CompletedTask);

            var result = await _service.UpdateSsoCredentialStatusAsync(request);

            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task UpdateSsoCredentialStatusAsync_WithInvalidId_ReturnsError(string itemId)
        {
            var request = new UpdateSsoCredentialStatusRequest { ItemId = itemId };

            var result = await _service.UpdateSsoCredentialStatusAsync(request);

            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public async Task GenerateUserCodeByClientAsync_WithValidClientId_GeneratesCode()
        {
            var request = new GenerateUserCodeRequest { ClientId = "client-123", CodeTtlInMinute = 60 };

            _authenticationRepository.Setup(x => x.SaveUserCodeByClientAsync(It.IsAny<UserCode>())).Returns(Task.CompletedTask);

            var result = await _service.GenerateUserCodeByClientAsync(request);

            Assert.True(result.IsSuccess);
            _authenticationRepository.Verify(x => x.SaveUserCodeByClientAsync(It.IsAny<UserCode>()), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GenerateUserCodeByClientAsync_WithInvalidClientId_ReturnsError(string clientId)
        {
            var request = new GenerateUserCodeRequest { ClientId = clientId };

            var result = await _service.GenerateUserCodeByClientAsync(request);

            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public async Task SaveClientCredentialAsync_WithValidName_SavesCredential()
        {
            var request = new SaveClientCredentialRequest { Name = "Test Client", Roles = new List<string> { "role1" } };
            var tenant = new Tenant
            {
                ApplicationDomain = "test-domain.com",
                DbConnectionString = "test-connection-string",
                JwtTokenParameters = new JwtTokenParameters
                {
                    Audiences = new List<string> { "audience1" },
                    PrivateCertificatePassword = "test-password",
                    IssueDate = DateTime.UtcNow
                }
            };

            _tenants.Setup(x => x.GetTenantByID("tenant-123")).Returns(tenant);
            _authenticationRepository.Setup(x => x.SaveClientCredentialAsync(It.IsAny<ClientCredential>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await _service.SaveClientCredentialAsync(request);

            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SaveClientCredentialAsync_WithInvalidName_ReturnsError(string name)
        {
            var request = new SaveClientCredentialRequest { Name = name };

            var result = await _service.SaveClientCredentialAsync(request);

            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public async Task DeleteClientCredentialAsync_DeletesCredential()
        {
            var request = new DeleteClientCredentialRequest { ItemId = "test-id" };

            _authenticationRepository.Setup(x => x.DeleteClientCredentialAsync(request)).Returns(Task.CompletedTask);

            var result = await _service.DeleteClientCredentialAsync(request);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task GetClientCredentialsAsync_ReturnsCredentials()
        {
            var request = new GetAllClientCredentialsRequest();
            var credentials = new List<ClientCredential> { new ClientCredential { ItemId = "test-id" } };

            _authenticationRepository.Setup(x => x.GetClientCredentialsAsync()).ReturnsAsync(credentials);

            var result = await _service.GetClientCredentialsAsync(request);

            Assert.Equal(credentials, result);
        }
    }
}