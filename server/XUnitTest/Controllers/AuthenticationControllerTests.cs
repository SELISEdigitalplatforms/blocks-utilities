using Api.Controllers;
using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using DomainService.Authentication;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.RequestModel;
using DomainService.Services;
using DomainService.Shared.RequestModel;
using DomainService.Shared.ResponseModel;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;

namespace XUnitTest.Controllers
{
    public class AuthenticationControllerTests
    {
        private readonly Mock<IOAuthTokenProvider> _tokenProvider = new();
        private readonly Mock<IAuthenticationService> _authService = new();
        private readonly Mock<IAuthenticationDomainService> _domainService = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IConfiguration> _config = new();
        private readonly Mock<IConfigurationService> _cloudConfig = new();
        private readonly Mock<ChangeControllerContext> _context = new(new Mock<ITenants>().Object, new Mock<IDbContextProvider>().Object, new Mock<IHttpContextAccessor>().Object);
        private readonly AuthenticationController _controller;
        private readonly DefaultHttpContext _httpContext;

        public AuthenticationControllerTests()  
        {
            _controller = new AuthenticationController(_tokenProvider.Object, _authService.Object, _config.Object, _domainService.Object, _repo.Object, _context.Object, _cloudConfig.Object);
            _httpContext = new DefaultHttpContext();
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = _httpContext
            };
        }

        [Fact]
        public async Task Logout_Should_Return_Ok_When_Success()
        {
            _authService
                .Setup(x => x.LogoutUser(It.IsAny<string>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(new LogoutResponse { IsSuccess = true });

            var result = await _controller.Logout(new LogoutRequest
            {
                RefreshToken = "token"
            });

            object okObjectResult = result.Should().BeOfType<OkObjectResult>();
            _authService.Verify(x => x.DeleteCookie(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Fact]
        public async Task Logout_Should_Return_BadRequest_When_Failed()
        {

            _authService
                .Setup(x => x.LogoutUser(It.IsAny<string>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(new LogoutResponse { IsSuccess = false });

            var result = await _controller.Logout(new LogoutRequest
            {
                RefreshToken = "token"
            });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task LogoutAll_WhenLogoutSucceeds_ReturnsOkAndDeletesCookie()
        {
            // Arrange
            var successResponse = new LogoutResponse
            {
                IsSuccess = true,
            };

            _authService
                .Setup(x => x.LogoutUser(string.Empty, It.IsAny<HttpRequest>()))
                .ReturnsAsync(successResponse);

            _authService
                .Setup(x => x.DeleteCookie(It.IsAny<HttpRequest>()))
                .Verifiable();

            // Act
            var result = await _controller.LogoutAll();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<LogoutResponse>(okResult.Value);
            Assert.True(response.IsSuccess);

            _authService.Verify(
                x => x.LogoutUser(string.Empty, It.IsAny<HttpRequest>()),
                Times.Once);
            _authService.Verify(
                x => x.DeleteCookie(It.IsAny<HttpRequest>()),
                Times.Once);
        }

        [Fact]
        public async Task LogoutAll_WhenLogoutFails_ReturnsBadRequestAndDoesNotDeleteCookie()
        {
            // Arrange
            var failureResponse = new LogoutResponse
            {
                IsSuccess = false,
            };

            _authService
                .Setup(x => x.LogoutUser(string.Empty, It.IsAny<HttpRequest>()))
                .ReturnsAsync(failureResponse);

            // Act
            var result = await _controller.LogoutAll();

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var response = Assert.IsType<LogoutResponse>(badRequestResult.Value);
            Assert.False(response.IsSuccess);

            _authService.Verify(
                x => x.LogoutUser(string.Empty, It.IsAny<HttpRequest>()),
                Times.Once);
            _authService.Verify(
                x => x.DeleteCookie(It.IsAny<HttpRequest>()),
                Times.Never);
        }

        [Fact]
        public async Task LogoutAll_PassesEmptyStringToLogoutUser()
        {
            // Arrange
            var successResponse = new LogoutResponse { IsSuccess = true };

            string? capturedRefreshToken = null;

            _authService
                .Setup(x => x.LogoutUser(It.IsAny<string>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(successResponse)
                .Callback<string, HttpRequest>((token, req) => capturedRefreshToken = token);

            // Act
            await _controller.LogoutAll();

            // Assert
            Assert.Equal(string.Empty, capturedRefreshToken);
            _authService.Verify(
                x => x.LogoutUser(string.Empty, It.IsAny<HttpRequest>()),
                Times.Once);
        }

        [Fact]
        public async Task LogoutAll_PassesCorrectHttpRequestToLogoutUser()
        {
            // Arrange
            var successResponse = new LogoutResponse { IsSuccess = true };

            HttpRequest? capturedRequest = null;

            _authService
                .Setup(x => x.LogoutUser(It.IsAny<string>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(successResponse)
                .Callback<string, HttpRequest>((token, req) => capturedRequest = req);

            // Act
            await _controller.LogoutAll();

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.Same(_httpContext.Request, capturedRequest);
        }

        [Fact]
        public async Task LogoutAll_PassesCorrectHttpRequestToDeleteCookie()
        {
            // Arrange
            var successResponse = new LogoutResponse { IsSuccess = true };

            HttpRequest? capturedRequest = null;

            _authService
                .Setup(x => x.LogoutUser(It.IsAny<string>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(successResponse);

            _authService
                .Setup(x => x.DeleteCookie(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>((req) => capturedRequest = req);

            // Act
            await _controller.LogoutAll();

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.Same(_httpContext.Request, capturedRequest);
        }

        [Fact]
        public async Task LogoutAll_WhenIsSuccessIsFalse_DoesNotCallDeleteCookie()
        {
            // Arrange
            var failureResponse = new LogoutResponse
            {
                IsSuccess = false,
            };

            _authService
                .Setup(x => x.LogoutUser(string.Empty, It.IsAny<HttpRequest>()))
                .ReturnsAsync(failureResponse);

            // Act
            await _controller.LogoutAll();

            // Assert
            _authService.Verify(
                x => x.DeleteCookie(It.IsAny<HttpRequest>()),
                Times.Never,
                "DeleteCookie should not be called when logout fails");
        }

        [Fact]
        public async Task LogoutAll_ReturnsCorrectResponseType_WhenSuccessful()
        {
            // Arrange
            var successResponse = new LogoutResponse
            {
                IsSuccess = true,
            };

            _authService
                .Setup(x => x.LogoutUser(string.Empty, It.IsAny<HttpRequest>()))
                .ReturnsAsync(successResponse);

            // Act
            var result = await _controller.LogoutAll();

            // Assert
            Assert.IsAssignableFrom<IActionResult>(result);
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, okResult.StatusCode);
        }

        [Fact]
        public async Task LogoutAll_ReturnsCorrectResponseType_WhenFailed()
        {
            // Arrange
            var failureResponse = new LogoutResponse
            {
                IsSuccess = false
            };

            _authService
                .Setup(x => x.LogoutUser(string.Empty, It.IsAny<HttpRequest>()))
                .ReturnsAsync(failureResponse);

            // Act
            var result = await _controller.LogoutAll();

            // Assert
            Assert.IsAssignableFrom<IActionResult>(result);
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, badRequestResult.StatusCode);
        }

        [Fact]
        public async Task SaveOIDCClient_WithValidRequest_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new SaveOIDCClientRequest
            {
                ItemId = "test-client-id",
                ClientDisplayName = "Test Client",
                ClientLogoUrl = "https://example.com/callback",
                ClientBrandColor = "test-client-color"
            };

            var expectedResponse = new SaveOIDCClientResponse
            {
                ItemId = "test-client-id",
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.SaveOIDCClientAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.SaveOIDCClient(request);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Equal("test-client-id", result.ItemId);
        }

        [Fact]
        public async Task SaveOIDCClient_CallsSaveOIDCClientAsyncWithCorrectRequest()
        {
            // Arrange
            var request = new SaveOIDCClientRequest
            {
                ItemId = "test-client-id",
                ClientDisplayName = "Test Client",
                ClientLogoUrl = "https://example.com/callback",
                ClientBrandColor = "test-client-color"
            };

            var expectedResponse = new SaveOIDCClientResponse
            {
                ItemId = "test-client-id",
                IsSuccess = true
            };
            
            SaveOIDCClientRequest? capturedRequest = null;
            _domainService
                .Setup(x => x.SaveOIDCClientAsync(It.IsAny<SaveOIDCClientRequest>()))
                .ReturnsAsync(expectedResponse)
                .Callback<SaveOIDCClientRequest>(r => capturedRequest = r);

            // Act
            await _controller.SaveOIDCClient(request);

            // Assert
            _domainService.Verify(
                x => x.SaveOIDCClientAsync(It.IsAny<SaveOIDCClientRequest>()),
                Times.Once);
            Assert.NotNull(capturedRequest);
            Assert.Same(request, capturedRequest);
            Assert.Equal("test-client-id", capturedRequest.ItemId);
            Assert.Equal("Test Client", capturedRequest.ClientDisplayName);
        }

        [Fact]
        public async Task SaveOIDCClient_WhenSaveFails_ReturnsFailureResponse()
        {
            // Arrange
            var request = new SaveOIDCClientRequest
            {
                ItemId = "test-client-id",
                ClientDisplayName = "Test Client",
                ClientLogoUrl = "https://example.com/callback",
                ClientBrandColor = "test-client-color"
            };

            var expectedResponse = new SaveOIDCClientResponse
            {
                ItemId = "test-client-id",
                IsSuccess = false
            };

            _domainService
                .Setup(x => x.SaveOIDCClientAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.SaveOIDCClient(request);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task SaveOIDCClient_ReturnsResponseFromDomainService()
        {
            // Arrange
            var request = new SaveOIDCClientRequest
            {
                ItemId = "test-client-id",
                ClientDisplayName = "Test Client",
                ClientLogoUrl = "https://example.com/callback",
                ClientBrandColor = "test-client-color"
            };

            var domainServiceResponse = new SaveOIDCClientResponse
            {
                ItemId = "client-123",
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.SaveOIDCClientAsync(request))
                .ReturnsAsync(domainServiceResponse);

            // Act
            var result = await _controller.SaveOIDCClient(request);

            // Assert
            Assert.Same(domainServiceResponse, result);
        }

        [Fact]
        public async Task SaveOIDCClient_WithNewClient_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new SaveOIDCClientRequest
            {
                ItemId = "test-client-id",
                ClientDisplayName = "Test Client",
                ClientLogoUrl = "https://example.com/callback",
                ClientBrandColor = "test-client-color"
            };

            var expectedResponse = new SaveOIDCClientResponse
            {
                IsSuccess = true,
                ItemId = "newly-generated-client-id"
            };

            _domainService
                .Setup(x => x.SaveOIDCClientAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.SaveOIDCClient(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("newly-generated-client-id", result.ItemId);
        }

        [Fact]
        public async Task SaveOIDCClient_WithExistingClient_ReturnsUpdateSuccessResponse()
        {
            // Arrange
            var request = new SaveOIDCClientRequest
            {
                ItemId = "test-client-id",
                ClientDisplayName = "Test Client",
                ClientLogoUrl = "https://example.com/callback",
                ClientBrandColor = "test-client-color"
            };

            var expectedResponse = new SaveOIDCClientResponse
            {
                IsSuccess = true,
                ItemId = "existing-client-id"
            };

            _domainService
                .Setup(x => x.SaveOIDCClientAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.SaveOIDCClient(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("existing-client-id", result.ItemId);
        }

        [Fact]
        public async Task SaveOIDCClient_WithValidationError_ReturnsFailureResponse()
        {
            // Arrange
            var request = new SaveOIDCClientRequest
            {
                ItemId = "Invalid Client"
                // Missing required fields
            };

            var expectedResponse = new SaveOIDCClientResponse
            {
                IsSuccess = false,
                ItemId = "Invalid Client"
            };

            _domainService
                .Setup(x => x.SaveOIDCClientAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.SaveOIDCClient(request);

            // Assert
            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task SaveOIDCClient_ReturnsCorrectResponseType()
        {
            // Arrange
            var request = new SaveOIDCClientRequest
            {
                ItemId = "test-client-id",
                ClientDisplayName = "Test Client",
                ClientLogoUrl = "https://example.com/callback",
                ClientBrandColor = "test-client-color"
            };
            var expectedResponse = new SaveOIDCClientResponse
            {
                ItemId = "test-client-id",
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.SaveOIDCClientAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.SaveOIDCClient(request);

            // Assert
            Assert.IsType<SaveOIDCClientResponse>(result);
        }
  
        [Fact]
        public async Task GetOIDCClient_WhenClientNotFound_ReturnsFailureResponse()
        {
            // Arrange
            var request = new GetOIDCClientRequest
            {
                ClientId = "non-existent-client"
            };

            var expectedResponse = new GetOIDCClientResponse
            {
                IsSuccess = false,
            };

            _domainService
                .Setup(x => x.GetOIDCClientAsyncAsync(request.ClientId))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.GetOIDCClient(request);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task GetOIDCClient_PassesCorrectRequestToGetOIDCClientAsyncAsync()
        {
            // Arrange
            var request = new GetOIDCClientRequest
            {
                ClientId = "test-client-123"
            };

            var expectedResponse = new GetOIDCClientResponse
            {
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.GetOIDCClientAsyncAsync(It.IsAny<string>()))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.GetOIDCClient(request);

            // Assert
            _domainService.Verify(
                x => x.GetOIDCClientAsyncAsync(request.ClientId),
                Times.Once,
                "GetOIDCClientAsyncAsync should be called once with the correct ClientId from the request");

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task Login_Should_Return_Unauthorized_When_Client_Invalid()
        {

            _authService
                .Setup(x => x.GetClientCredentialAsync(It.IsAny<string>()))
                .ReturnsAsync((OIDCClientCredential)null);

            var result = await _controller.Login(new LoginRequest
            {
                ClientId = "client",
                RedirectUri = "uri",
                Scope = "scope",
                State = "state",
                Nonce = "nonce"
            });

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Login_Should_Return_Ok_When_Authenticated()
        {

            _authService
                .Setup(x => x.GetClientCredentialAsync(It.IsAny<string>()))
                .ReturnsAsync(new OIDCClientCredential
                {
                    ItemId = "client",
                    RedirectUri = "uri",
                    Scope = "scope"
                });

            _tokenProvider
                .Setup(x => x.AuthenticateAsync(It.IsAny<TokenRequest>()))
                .ReturnsAsync(new OkResult());

            var result = await _controller.Login(new LoginRequest
            {
                ClientId = "client",
                RedirectUri = "uri",
                Scope = "scope",
                Username = "user",
                Password = "pass"
            });

            result.Should().BeOfType<OkResult>();
        }

        [Fact]
        public async Task GetUserInfo_Should_Return_Unauthorized_When_Token_Invalid()
        {

            _authService
                .Setup(x => x.GetPrincipalFromTokenAsync(
                    It.IsAny<HttpRequest>(),
                    It.IsAny<string>(),
                    false))
                .ReturnsAsync((ClaimsPrincipal)null);

            var result = await _controller.GetUserInfo();

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task GetUserInfo_Should_Return_Claims_When_Valid()
        {
            var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"),
             new Claim(ClaimTypes.Email, "test@test.com"),
             new Claim("name", "Test User")]));

            _authService
                .Setup(x => x.GetPrincipalFromTokenAsync(
                    It.IsAny<HttpRequest>(),
                    It.IsAny<string>(),
                    false))
                .ReturnsAsync(claims);

            var result = await _controller.GetUserInfo();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task LogoutUser_Should_Remove_Token_And_Update_Session()
        {
            var cache = new Mock<ICacheClient>();
            var repo = new Mock<IAuthenticationRepository>();
            var domain = new Mock<IAuthenticationDomainService>();
            var tenants = new Mock<ITenants>();
            var logger = new Mock<ILogger<AuthenticationService>>();

            repo.Setup(x => x.UpdateSessionStatusAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var service = new AuthenticationService(
                logger.Object,
                cache.Object,
                repo.Object,
                domain.Object,
                tenants.Object
            );

            var result = await service.LogoutUser("token", new DefaultHttpContext().Request);

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetOIDCClients_WithValidRequest_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new GetOIDCClientsRequest();

            var expectedResponse = new GetOIDCClientsResponse
            {
                oIDCClientCredentials = new List<OIDCClientCredential>
                {
                    new OIDCClientCredential
                    {
                        ItemId = "client-1",
                        ClientDisplayName = "Test Client 1",
                        ClientLogoUrl = "https://example.com/logo1.png",
                        ClientBrandColor = "#FF5733"
                    },
                    new OIDCClientCredential
                    {
                        ItemId = "client-2",
                        ClientDisplayName = "Test Client 2",
                        ClientLogoUrl = "https://example.com/logo2.png",
                        ClientBrandColor = "#33FF57"
                    }
                },
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.GetOIDCClientsAsyncAsync())
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.GetOIDCClients(request);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.oIDCClientCredentials);
            Assert.Equal(2, result.oIDCClientCredentials.Count);
        }

        [Fact]
        public async Task GetOIDCClients_CallsGetOIDCClientsAsyncAsync()
        {
            // Arrange
            var request = new GetOIDCClientsRequest();

            var expectedResponse = new GetOIDCClientsResponse
            {
                oIDCClientCredentials = new List<OIDCClientCredential>(),
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.GetOIDCClientsAsyncAsync())
                .ReturnsAsync(expectedResponse);

            // Act
            await _controller.GetOIDCClients(request);

            // Assert
            _domainService.Verify(
                x => x.GetOIDCClientsAsyncAsync(),
                Times.Once,
                "GetOIDCClientsAsyncAsync should be called once");
        }

        [Fact]
        public async Task GetOIDCClients_WithEmptyList_ReturnsSuccessWithEmptyList()
        {
            // Arrange
            var request = new GetOIDCClientsRequest();

            var expectedResponse = new GetOIDCClientsResponse
            {
                oIDCClientCredentials = new List<OIDCClientCredential>(),
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.GetOIDCClientsAsyncAsync())
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.GetOIDCClients(request);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.oIDCClientCredentials);
            Assert.Empty(result.oIDCClientCredentials);
        }

        [Fact]
        public async Task GetOIDCClients_ReturnsResponseFromDomainService()
        {
            // Arrange
            var request = new GetOIDCClientsRequest();

            var domainServiceResponse = new GetOIDCClientsResponse
            {
                oIDCClientCredentials = new List<OIDCClientCredential>
                {
                    new OIDCClientCredential
                    {
                        ItemId = "client-123",
                        ClientDisplayName = "Domain Client",
                        ClientLogoUrl = "https://example.com/logo.png",
                        ClientBrandColor = "#123456"
                    }
                },
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.GetOIDCClientsAsyncAsync())
                .ReturnsAsync(domainServiceResponse);

            // Act
            var result = await _controller.GetOIDCClients(request);

            // Assert
            Assert.Same(domainServiceResponse, result);
        }

        [Fact]
        public async Task DeleteOIDCClient_WithValidRequest_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new DeleteOIDCClientRequest
            {
                ItemId = "client-to-delete"
            };

            var expectedResponse = new BaseResponse
            {
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.DeleteOIDCClientAsyncAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.DeleteOIDCClient(request);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task DeleteOIDCClient_CallsDeleteOIDCClientAsyncAsyncWithCorrectRequest()
        {
            // Arrange
            var request = new DeleteOIDCClientRequest
            {
                ItemId = "client-to-delete"
            };

            var expectedResponse = new BaseResponse
            {
                IsSuccess = true
            };

            DeleteOIDCClientRequest? capturedRequest = null;
            _domainService
                .Setup(x => x.DeleteOIDCClientAsyncAsync(It.IsAny<DeleteOIDCClientRequest>()))
                .ReturnsAsync(expectedResponse)
                .Callback<DeleteOIDCClientRequest>(r => capturedRequest = r);

            // Act
            await _controller.DeleteOIDCClient(request);

            // Assert
            _domainService.Verify(
                x => x.DeleteOIDCClientAsyncAsync(It.IsAny<DeleteOIDCClientRequest>()),
                Times.Once,
                "DeleteOIDCClientAsyncAsync should be called once with the correct request");
            Assert.NotNull(capturedRequest);
            Assert.Same(request, capturedRequest);
            Assert.Equal("client-to-delete", capturedRequest.ItemId);
        }

        [Fact]
        public async Task DeleteOIDCClient_WhenDeleteFails_ReturnsFailureResponse()
        {
            // Arrange
            var request = new DeleteOIDCClientRequest
            {
                ItemId = "non-existent-client"
            };

            var expectedResponse = new BaseResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "client_not_found", "The specified OIDC client does not exist" }
                }
            };

            _domainService
                .Setup(x => x.DeleteOIDCClientAsyncAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.DeleteOIDCClient(request);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Errors);
            Assert.True(result.Errors.ContainsKey("client_not_found"));
        }

        [Fact]
        public async Task DeleteOIDCClient_ReturnsResponseFromDomainService()
        {
            // Arrange
            var request = new DeleteOIDCClientRequest
            {
                ItemId = "client-123"
            };

            var domainServiceResponse = new BaseResponse
            {
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.DeleteOIDCClientAsyncAsync(request))
                .ReturnsAsync(domainServiceResponse);

            // Act
            var result = await _controller.DeleteOIDCClient(request);

            // Assert
            Assert.Same(domainServiceResponse, result);
        }

        [Fact]
        public async Task GenerateUserCode_WithValidRequest_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new GenerateUserCodeRequest
            {
                ClientId = "test-client-id",
                CodeTtlInMinute = 10080,
                Note = "Test user code generation"
            };

            var expectedResponse = new BaseResponse
            {
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.GenerateUserCodeByClientAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.GenerateUserCode(request);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task GenerateUserCode_CallsGenerateUserCodeByClientAsyncWithCorrectRequest()
        {
            // Arrange
            var request = new GenerateUserCodeRequest
            {
                ClientId = "test-client-id",
                CodeTtlInMinute = 7200,
                Note = "5 day TTL user code"
            };

            var expectedResponse = new BaseResponse
            {
                IsSuccess = true
            };

            GenerateUserCodeRequest? capturedRequest = null;
            _domainService
                .Setup(x => x.GenerateUserCodeByClientAsync(It.IsAny<GenerateUserCodeRequest>()))
                .ReturnsAsync(expectedResponse)
                .Callback<GenerateUserCodeRequest>(r => capturedRequest = r);

            // Act
            await _controller.GenerateUserCode(request);

            // Assert
            _domainService.Verify(
                x => x.GenerateUserCodeByClientAsync(It.IsAny<GenerateUserCodeRequest>()),
                Times.Once,
                "GenerateUserCodeByClientAsync should be called once with the correct request");
            Assert.NotNull(capturedRequest);
            Assert.Same(request, capturedRequest);
            Assert.Equal("test-client-id", capturedRequest.ClientId);
            Assert.Equal(7200, capturedRequest.CodeTtlInMinute);
            Assert.Equal("5 day TTL user code", capturedRequest.Note);
        }

        [Fact]
        public async Task GenerateUserCode_WhenGenerationFails_ReturnsFailureResponse()
        {
            // Arrange
            var request = new GenerateUserCodeRequest
            {
                ClientId = "invalid-client",
                CodeTtlInMinute = 1440,
                Note = "Test note"
            };

            var expectedResponse = new BaseResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "invalid_client", "The specified client is invalid or does not exist" }
                }
            };

            _domainService
                .Setup(x => x.GenerateUserCodeByClientAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.GenerateUserCode(request);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Errors);
            Assert.True(result.Errors.ContainsKey("invalid_client"));
        }

        [Fact]
        public async Task GenerateUserCode_ReturnsResponseFromDomainService()
        {
            // Arrange
            var request = new GenerateUserCodeRequest
            {
                ClientId = "client-456",
                CodeTtlInMinute = 1440,
                Note = "24 hour user code"
            };

            var domainServiceResponse = new BaseResponse
            {
                IsSuccess = true
            };

            _domainService
                .Setup(x => x.GenerateUserCodeByClientAsync(request))
                .ReturnsAsync(domainServiceResponse);

            // Act
            var result = await _controller.GenerateUserCode(request);

            // Assert
            Assert.Same(domainServiceResponse, result);
        }

        [Fact]
        public async Task GetUserCodes_WithExistingCodes_ReturnsListOfUserCodes()
        {
            // Arrange
            var userId = "test-user-123";
            var expectedUserCodes = new List<GetUserCodesByUserIdResponse>
            {
                new GetUserCodesByUserIdResponse
                {
                    ItemId = "code-1",
                    Code = "ABC123",
                    ClientId = "client-1",
                    UserId = userId,
                    CodeTtlInMinute = 10080,
                    ExpiryDate = DateTime.UtcNow.AddDays(7),
                    Note = "Test code 1",
                    CreatedDate = DateTime.UtcNow
                },
                new GetUserCodesByUserIdResponse
                {
                    ItemId = "code-2",
                    Code = "DEF456",
                    ClientId = "client-2",
                    UserId = userId,
                    CodeTtlInMinute = 7200,
                    ExpiryDate = DateTime.UtcNow.AddDays(5),
                    Note = "Test code 2",
                    CreatedDate = DateTime.UtcNow
                }
            };

            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: userId,
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@test.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant"
            );

            BlocksContext.SetContext(blocksContext, true);

            _repo
                .Setup(x => x.GetUserCodesByUserIdAsync(userId))
                .ReturnsAsync(expectedUserCodes);

            // Act
            var result = await _controller.GetUserCodes();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("ABC123", result[0].Code);
            Assert.Equal("client-1", result[0].ClientId);
            Assert.Equal("DEF456", result[1].Code);
            Assert.Equal("client-2", result[1].ClientId);
        }

        [Fact]
        public async Task GetUserCodes_WithNoExistingCodes_ReturnsEmptyList()
        {
            // Arrange
            var userId = "test-user-456";
            var emptyList = new List<GetUserCodesByUserIdResponse>();

            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: userId,
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@test.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant"
            );

            BlocksContext.SetContext(blocksContext, true);

            _repo
                .Setup(x => x.GetUserCodesByUserIdAsync(userId))
                .ReturnsAsync(emptyList);

            // Act
            var result = await _controller.GetUserCodes();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetUserCodes_CallsRepositoryWithCorrectUserId()
        {
            // Arrange
            var userId = "test-user-789";
            var userCodes = new List<GetUserCodesByUserIdResponse>();

            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: userId,
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@test.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant"
            );

            BlocksContext.SetContext(blocksContext, true);

            string? capturedUserId = null;
            _repo
                .Setup(x => x.GetUserCodesByUserIdAsync(It.IsAny<string>()))
                .ReturnsAsync(userCodes)
                .Callback<string>(id => capturedUserId = id);

            // Act
            await _controller.GetUserCodes();

            // Assert
            _repo.Verify(
                x => x.GetUserCodesByUserIdAsync(userId),
                Times.Once,
                "GetUserCodesByUserIdAsync should be called once with the correct user ID from BlocksContext");
            Assert.Equal(userId, capturedUserId);
        }

       



        [Fact]
        public async Task GetClientCredentials_WithExistingCredentials_ReturnsListOfCredentials()
        {
            // Arrange
            var request = new GetAllClientCredentialsRequest();

            var expectedCredentials = new List<ClientCredential>
            {
                new ClientCredential
                {
                    ItemId = "cred-1",
                    Name = "API Client 1",
                    ClientSecret = "secret-1",
                    Roles = new List<string> { "admin", "user" },
                    IsActive = true,
                    Audiences = new List<string> { "api.example.com" }
                },
                new ClientCredential
                {
                    ItemId = "cred-2",
                    Name = "API Client 2",
                    ClientSecret = "secret-2",
                    Roles = new List<string> { "read-only" },
                    IsActive = true,
                    Audiences = new List<string> { "app.example.com" }
                }
            };

            _repo
                .Setup(x => x.GetClientCredentialsAsync())
                .ReturnsAsync(expectedCredentials);

            // Act
            var result = await _controller.GetClientCredentials(request);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("API Client 1", result[0].Name);
            Assert.Equal("cred-1", result[0].ItemId);
            Assert.Equal("API Client 2", result[1].Name);
            Assert.Equal("cred-2", result[1].ItemId);
            Assert.True(result[0].IsActive);
            Assert.Equal(2, result[0].Roles.Count);
        }

        [Fact]
        public async Task GetClientCredentials_WithNoExistingCredentials_ReturnsEmptyList()
        {
            // Arrange
            var request = new GetAllClientCredentialsRequest();
            var emptyList = new List<ClientCredential>();

            _repo
                .Setup(x => x.GetClientCredentialsAsync())
                .ReturnsAsync(emptyList);

            // Act
            var result = await _controller.GetClientCredentials(request);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            _repo.Verify(
                x => x.GetClientCredentialsAsync(),
                Times.Once,
                "GetClientCredentialsAsync should be called once");
        }

        [Fact]
        public async Task Authorize_WithValidCodeResponseType_ReturnsRedirectWithCorrectUri()
        {
            // Arrange
            var request = new AuthorizeRequest
            {
                ResponseType = "code",
                ClientId = "test-client-id",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile",
                State = "test-state",
                Nonce = "test-nonce"
            };

            var clientCredential = new OIDCClientCredential
            {
                ItemId = "test-client-id",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile",
                ClientLogoUrl = "https://example.com/logo.png",
                ClientBrandColor = "#FF5733"
            };

            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: "test-user",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@test.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant"
            );

            BlocksContext.SetContext(blocksContext, true);

            _authService
                .Setup(x => x.GetClientCredentialAsync(request.ClientId))
                .ReturnsAsync(clientCredential);

            _authService
                .Setup(x => x.GetPrincipalFromTokenAsync(It.IsAny<HttpRequest>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync((ClaimsPrincipal)null);

            _config
                .Setup(x => x["OpenIdConnect:RedirectUri"])
                .Returns("https://idp.example.com/login");

            // Act
            var result = await _controller.Authorize(request);

            // Assert
            var redirectResult = Assert.IsType<RedirectResult>(result);
            Assert.NotNull(redirectResult.Url);
            Assert.Contains("https://idp.example.com/login", redirectResult.Url);
            Assert.Contains("x-blocks-key=test-tenant", redirectResult.Url);
            Assert.Contains("clientId=test-client-id", redirectResult.Url);
            Assert.Contains("brandColor=#FF5733", redirectResult.Url);
            Assert.Contains("logoUrl=https://example.com/logo.png", redirectResult.Url);
            Assert.Contains("state=test-state", redirectResult.Url);
            Assert.Contains("redirect_uri=https://example.com/callback", redirectResult.Url);
            Assert.Contains("scope=openid profile", redirectResult.Url);
        }

        [Fact]
        public async Task Authorize_WithInvalidClient_ReturnsRedirectToErrorPage()
        {
            // Arrange
            var request = new AuthorizeRequest
            {
                ResponseType = "code",
                ClientId = "invalid-client-id",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile"
            };

            _authService
                .Setup(x => x.GetClientCredentialAsync(request.ClientId))
                .ReturnsAsync((OIDCClientCredential)null);

            _config
                .Setup(x => x["OpenIdConnect:ErrorPageRedirectonUri"])
                .Returns("https://idp.example.com/error");

            // Act
            var result = await _controller.Authorize(request);

            // Assert
            var redirectResult = Assert.IsType<RedirectResult>(result);
            Assert.NotNull(redirectResult.Url);
            Assert.Contains("https://idp.example.com/error", redirectResult.Url);
            _authService.Verify(
                x => x.GetClientCredentialAsync(request.ClientId),
                Times.Once,
                "GetClientCredentialAsync should be called once with the correct client ID");
        }

        [Fact]
        public async Task Token_WithValidTokenPayload_ReturnsOkResult()
        {
            // Arrange
            var tokenPayload = new TokenPayload
            {
                GrantType = "authorization_code",
                Code = "auth-code-123",
                RedirectUri = "https://example.com/callback",
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret",
                Username = "testuser",
                Password = "testpassword",
                Scope = "openid profile",
                RememberMe = true,
                RefreshToken = "refresh-token-123",
                State = "test-state",
                Language = "en",
                BiometricId = "bio-id-123",
                BiometriKey = "bio-key-123",
                UserSecret = "user-secret-123",
                OrganizationId = "org-123",
                MfaId = "mfa-id-123",
                MfaType = UserMfaType.TOTP
            };

            var expectedResult = new OkObjectResult(new { access_token = "access-token-123", token_type = "Bearer" });

            _tokenProvider
                .Setup(x => x.AuthenticateAsync(It.IsAny<TokenRequest>()))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.Token(tokenPayload);

            // Assert
            Assert.NotNull(result);
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
            _tokenProvider.Verify(
                x => x.AuthenticateAsync(It.IsAny<TokenRequest>()),
                Times.Once,
                "AuthenticateAsync should be called once");
        }

        [Fact]
        public async Task Token_MapsTokenPayloadToTokenRequestCorrectly()
        {
            // Arrange
            var tokenPayload = new TokenPayload
            {
                GrantType = "password",
                Code = "code-456",
                RedirectUri = "https://app.example.com/callback",
                ClientId = "client-456",
                ClientSecret = "secret-456",
                Username = "user@example.com",
                Password = "pass123",
                Scope = "openid email profile",
                RememberMe = false,
                RefreshToken = "refresh-456",
                State = "state-456",
                Language = "fr",
                BiometricId = "bio-456",
                BiometriKey = "biokey-456",
                UserSecret = "usersecret-456",
                OrganizationId = "org-456",
                MfaId = "mfa-456",
                MfaType = UserMfaType.Sms
            };

            TokenRequest? capturedTokenRequest = null;
            var expectedResult = new OkResult();

            _tokenProvider
                .Setup(x => x.AuthenticateAsync(It.IsAny<TokenRequest>()))
                .ReturnsAsync(expectedResult)
                .Callback<TokenRequest>(req => capturedTokenRequest = req);

            // Act
            await _controller.Token(tokenPayload);

            // Assert
            Assert.NotNull(capturedTokenRequest);
            Assert.Equal(tokenPayload.GrantType, capturedTokenRequest.GrantType);
            Assert.Equal(tokenPayload.Code, capturedTokenRequest.Code);
            Assert.Equal(tokenPayload.RedirectUri, capturedTokenRequest.RedirectUri);
            Assert.Equal(tokenPayload.ClientId, capturedTokenRequest.ClientId);
            Assert.Equal(tokenPayload.ClientSecret, capturedTokenRequest.ClientSecret);
            Assert.Equal(tokenPayload.Username, capturedTokenRequest.Username);
            Assert.Equal(tokenPayload.Password, capturedTokenRequest.Password);
            Assert.Equal(tokenPayload.Scope, capturedTokenRequest.Scope);
            Assert.Equal(tokenPayload.RememberMe, capturedTokenRequest.RememberMe);
            Assert.Equal(tokenPayload.RefreshToken, capturedTokenRequest.RefreshToken);
            Assert.Equal(tokenPayload.State, capturedTokenRequest.State);
            Assert.Equal(tokenPayload.Language, capturedTokenRequest.Language);
            Assert.Equal(tokenPayload.BiometricId, capturedTokenRequest.BiometricId);
            Assert.Equal(tokenPayload.BiometriKey, capturedTokenRequest.BiometricKey);
            Assert.Equal(tokenPayload.UserSecret, capturedTokenRequest.UserCode);
            Assert.Equal(tokenPayload.OrganizationId, capturedTokenRequest.OrganizationId);
            Assert.Equal(tokenPayload.MfaId, capturedTokenRequest.MfaId);
            Assert.Equal(tokenPayload.MfaType, capturedTokenRequest.MfaType);
            Assert.NotNull(capturedTokenRequest.Request);
            Assert.Same(_httpContext.Request, capturedTokenRequest.Request);
        }

        [Fact]
        public async Task GetSocialLogInEndPoint_WithValidProviderUrl_ReturnsRedirect()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "Google",
                Audience = "https://example.com",
                NextUrl = "https://example.com/dashboard",
                SendAsResponse = false
            };

            var expectedResponse = new GetSocialLogInEndPointResponse
            {
                ProviderUrl = "https://accounts.google.com/o/oauth2/v2/auth?client_id=test&redirect_uri=callback",
                IsAResponse = false,
                Error = null
            };

            _tokenProvider
                .Setup(x => x.GetSocialLogInEndPointAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.GetSocialLogInEndPoint(request);

            // Assert
            var redirectResult = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth?client_id=test&redirect_uri=callback", redirectResult.Url);
            _tokenProvider.Verify(
                x => x.GetSocialLogInEndPointAsync(request),
                Times.Once,
                "GetSocialLogInEndPointAsync should be called once with the correct request");
        }

        [Fact]
        public async Task GetSocialLogInEndPoint_WithIsAResponseTrue_ReturnsOkObjectResult()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "Microsoft",
                Audience = "https://example.com",
                NextUrl = "https://example.com/home",
                SendAsResponse = true
            };

            var expectedResponse = new GetSocialLogInEndPointResponse
            {
                ProviderUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
                IsAResponse = true,
                Error = null
            };

            _tokenProvider
                .Setup(x => x.GetSocialLogInEndPointAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.GetSocialLogInEndPoint(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
            var response = Assert.IsType<GetSocialLogInEndPointResponse>(okResult.Value);
            Assert.Equal(expectedResponse.ProviderUrl, response.ProviderUrl);
            Assert.True(response.IsAResponse);
            _tokenProvider.Verify(
                x => x.GetSocialLogInEndPointAsync(request),
                Times.Once,
                "GetSocialLogInEndPointAsync should be called once");
        }

        [Fact]
        public async Task GetSocialLogInEndPoint_WithEmptyProviderUrl_ReturnsBadRequest()
        {
            // Arrange
            var request = new GetSocialLogInEndPointRequest
            {
                Provider = "InvalidProvider",
                Audience = "https://example.com",
                NextUrl = "https://example.com/callback",
                SendAsResponse = false
            };

            var expectedResponse = new GetSocialLogInEndPointResponse
            {
                ProviderUrl = null,
                IsAResponse = false,
                Error = "Invalid provider configuration"
            };

            _tokenProvider
                .Setup(x => x.GetSocialLogInEndPointAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.GetSocialLogInEndPoint(request);

            // Assert
            Assert.NotNull(result);
            var objectResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, objectResult.StatusCode);
            _tokenProvider.Verify(
                x => x.GetSocialLogInEndPointAsync(request),
                Times.Once,
                "GetSocialLogInEndPointAsync should be called once even when provider is invalid");
        }

        [Fact]
        public async Task UserAcknowledgement_WhenNotAcknowledged_ReturnsRedirectUri()
        {
            // Arrange
            var request = new AcknowledgeRequest
            {
                ClientId = "test-client-id",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile",
                State = "test-state",
                Nonce = "test-nonce",
                IsAcknowledged = false,
                Username = "testuser"
            };

            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: "test-user",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "test@test.com",
                permissions: Array.Empty<string>(),
                userName: "testuser",
                phoneNumber: string.Empty,
                displayName: "Test User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant"
            );

            BlocksContext.SetContext(blocksContext, true);

            // Act
            var result = await _controller.UserAcknowledgement(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
            var response = okResult.Value;
            var redirectUrlProperty = response.GetType().GetProperty("redirectUrl");
            Assert.NotNull(redirectUrlProperty);
            var redirectUrl = redirectUrlProperty.GetValue(response)?.ToString();
            Assert.Equal(request.RedirectUri, redirectUrl);
        }

        [Fact]
        public async Task UserAcknowledgement_WhenAcknowledgedAndUserMatches_ReturnsConstructedRedirectUri()
        {
            // Arrange
            var request = new AcknowledgeRequest
            {
                ClientId = "test-client-id",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile email",
                State = "state-123",
                Nonce = "nonce-456",
                IsAcknowledged = true,
                Username = "authenticateduser"
            };

            var expectedConstructedUri = "https://example.com/callback?code=auth-code-123&state=state-123";

            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: "user-123",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "user@test.com",
                permissions: Array.Empty<string>(),
                userName: "authenticateduser",
                phoneNumber: string.Empty,
                displayName: "Authenticated User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant"
            );

            BlocksContext.SetContext(blocksContext, true);

            _authService
                .Setup(x => x.ConstructRedirectUriAsync(request.ClientId, request))
                .ReturnsAsync(expectedConstructedUri);

            // Act
            var result = await _controller.UserAcknowledgement(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
            var response = okResult.Value;
            var redirectUrlProperty = response.GetType().GetProperty("redirectUrl");
            Assert.NotNull(redirectUrlProperty);
            var redirectUrl = redirectUrlProperty.GetValue(response)?.ToString();
            Assert.Equal(expectedConstructedUri, redirectUrl);
            _authService.Verify(
                x => x.ConstructRedirectUriAsync(request.ClientId, request),
                Times.Once,
                "ConstructRedirectUriAsync should be called once with correct parameters");
        }

        [Fact]
        public async Task UserAcknowledgement_WhenAcknowledgedButUserMismatch_ReturnsBadRequest()
        {
            // Arrange
            var request = new AcknowledgeRequest
            {
                ClientId = "test-client-id",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile",
                State = "test-state",
                Nonce = "test-nonce",
                IsAcknowledged = true,
                Username = "differentuser"
            };

            var blocksContext = BlocksContext.Create(
                tenantId: "test-tenant",
                roles: Array.Empty<string>(),
                userId: "user-123",
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: string.Empty,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "user@test.com",
                permissions: Array.Empty<string>(),
                userName: "authenticateduser",
                phoneNumber: string.Empty,
                displayName: "Authenticated User",
                oauthToken: string.Empty,
                refreshToken: string.Empty,
                actualTentId: "test-tenant"
            );

            BlocksContext.SetContext(blocksContext, true);

            // Act
            var result = await _controller.UserAcknowledgement(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequestResult.Value);
            var response = badRequestResult.Value;
            var errorProperty = response.GetType().GetProperty("error");
            Assert.NotNull(errorProperty);
            var error = errorProperty.GetValue(response)?.ToString();
            Assert.Equal("invalid_user", error);
            _authService.Verify(
                x => x.ConstructRedirectUriAsync(It.IsAny<string>(), It.IsAny<AcknowledgeRequest>()),
                Times.Never,
                "ConstructRedirectUriAsync should not be called when user mismatch occurs");
        }

        [Fact]
        public async Task GetLoginOptions_ReturnsOkResultWithLoginOptions()
        {
            // Arrange
            var expectedResult = new OkObjectResult(new 
            { 
                loginOptions = new List<object>
                {
                    new { provider = "Google", displayName = "Google", iconUrl = "https://example.com/google-icon.png" },
                    new { provider = "Microsoft", displayName = "Microsoft", iconUrl = "https://example.com/ms-icon.png" }
                }
            });

            _authService
                .Setup(x => x.GetLoginOptionsAsync())
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.GetLoginOptions();

            // Assert
            Assert.NotNull(result);
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
            _authService.Verify(
                x => x.GetLoginOptionsAsync(),
                Times.Once,
                "GetLoginOptionsAsync should be called once");
        }

        [Fact]
        public async Task GetLoginOptions_ReturnsResponseFromAuthenticationService()
        {
            // Arrange
            var authServiceResponse = new OkObjectResult(new 
            { 
                ssoProviders = new List<object>
                {
                    new { name = "Azure AD", enabled = true },
                    new { name = "GitHub", enabled = true }
                },
                allowEmailPassword = true,
                allowBiometric = false
            });

            _authService
                .Setup(x => x.GetLoginOptionsAsync())
                .ReturnsAsync(authServiceResponse);

            // Act
            var result = await _controller.GetLoginOptions();

            // Assert
            Assert.Same(authServiceResponse, result);
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, okResult.StatusCode);
            _authService.Verify(
                x => x.GetLoginOptionsAsync(),
                Times.Once,
                "GetLoginOptionsAsync should be called exactly once");
        }

        [Fact]
        public async Task GetLoginOptions_WithNoSsoProviders_ReturnsEmptyList()
        {
            // Arrange
            var emptyResult = new OkObjectResult(new 
            { 
                loginOptions = new List<object>()
            });

            _authService
                .Setup(x => x.GetLoginOptionsAsync())
                .ReturnsAsync(emptyResult);

            // Act
            var result = await _controller.GetLoginOptions();

            // Assert
            Assert.NotNull(result);
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
            _authService.Verify(
                x => x.GetLoginOptionsAsync(),
                Times.Once,
                "GetLoginOptionsAsync should be called even when no SSO providers are configured");
        }

        [Fact]
        public async Task GetLoginOptions_ReturnsCorrectResponseType()
        {
            // Arrange
            var expectedResult = new OkObjectResult(new 
            { 
                providers = new[] { "Google", "Microsoft", "GitHub" }
            });

            _authService
                .Setup(x => x.GetLoginOptionsAsync())
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.GetLoginOptions();

            // Assert
            Assert.IsAssignableFrom<IActionResult>(result);
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, okResult.StatusCode);
        }
    }
}
