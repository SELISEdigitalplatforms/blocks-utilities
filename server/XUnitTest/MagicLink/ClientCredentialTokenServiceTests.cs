using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Service;

namespace XUnitTest.MagicLink
{
    public class ClientCredentialTokenServiceTests  
    {
        private readonly Mock<ILogger<ClientCredentialTokenService>> _mockLogger;
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly ClientCredentialTokenService _service;

        public ClientCredentialTokenServiceTests()
        {
            _mockLogger = new Mock<ILogger<ClientCredentialTokenService>>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockConfiguration = new Mock<IConfiguration>();

            _service = new ClientCredentialTokenService(
                _mockLogger.Object,
                _mockHttpClientFactory.Object,
                _mockConfiguration.Object
            );
        }

        [Fact]
        public async Task GetTokenAsync_ShouldReturnAccessToken_WhenRequestIsSuccessful()
        {
            // Arrange
            var clientCredentials = new ClientCredential { ItemId = "test-client", ClientSecret = "test-secret" };
            var projectKey = "test-project-key";
            var tokenResponse = new TokenResponse 
            { 
                AccessToken = "test-token", 
                ExpiresIn = 3600 ,
                TokenType = "Bearer",
                RefreshToken = "test-refresh",
                IdToken = "test-id-token"
            };

            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", 
                    ItExpr.IsAny<HttpRequestMessage>(), 
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(tokenResponse))
                });

            var httpClient = new HttpClient(mockHandler.Object);
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            _mockConfiguration.Setup(c => c["AuthenticationTokenEndpoint"]).Returns("https://test.api.com/token");

            // Act
            var result = await _service.GetTokenAsync(clientCredentials, projectKey);

            // Assert
            result.Should().Be("test-token");
        }

        [Fact]
        public async Task GetTokenAsync_ShouldReturnNull_WhenRequestFails()
        {
            // Arrange
            var clientCredentials = new ClientCredential { ItemId = "test-client", ClientSecret = "test-secret" };
            var projectKey = "test-project-key";

            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Unauthorized,
                    Content = new StringContent("Invalid credentials")
                });

            var httpClient = new HttpClient(mockHandler.Object);
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            // Act
            var result = await _service.GetTokenAsync(clientCredentials, projectKey);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetTokenAsync_ShouldReturnNull_WhenTokenResponseIsEmpty()
        {
            // Arrange
            var clientCredentials = new ClientCredential { ItemId = "test-client", ClientSecret = "test-secret" };
            var projectKey = "test-project-key";
            var tokenResponse = new TokenResponse { AccessToken = null };

            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(tokenResponse))
                });

            var httpClient = new HttpClient(mockHandler.Object);
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            // Act
            var result = await _service.GetTokenAsync(clientCredentials, projectKey);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetTokenAsync_ShouldReturnNull_WhenExceptionIsThrown()
        {
            // Arrange
            var clientCredentials = new ClientCredential { ItemId = "test-client", ClientSecret = "test-secret" };
            var projectKey = "test-project-key";

            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            var httpClient = new HttpClient(mockHandler.Object);
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            // Act
            var result = await _service.GetTokenAsync(clientCredentials, projectKey);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetTokenAsync_ShouldUseDefaultEndpoint_WhenConfigurationIsNull()
        {
            // Arrange
            var clientCredentials = new ClientCredential { ItemId = "test-client", ClientSecret = "test-secret" };
            var projectKey = "test-project-key";
            var tokenResponse = new TokenResponse { AccessToken = "test-token", ExpiresIn = 3600 };

            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString() == "https://api.seliseblocks.com/idp/v1/Authentication/token"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(tokenResponse))
                });

            var httpClient = new HttpClient(mockHandler.Object);
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            _mockConfiguration.Setup(c => c["AuthenticationTokenEndpoint"]).Returns((string?)null);

            // Act
            var result = await _service.GetTokenAsync(clientCredentials, projectKey);

            // Assert
            result.Should().Be("test-token");
        }
    }
}
