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
        private readonly Mock<IClientCredentialSecretResolver> _mockSecretResolver;
        private readonly ClientCredentialTokenService _service;

        public ClientCredentialTokenServiceTests()
        {
            _mockLogger = new Mock<ILogger<ClientCredentialTokenService>>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockSecretResolver = new Mock<IClientCredentialSecretResolver>();

            // By default the resolver answers the plaintext field, as it does for a legacy credential.
            _mockSecretResolver
                .Setup(r => r.GetSecretAsync(It.IsAny<ClientCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ClientCredential c, string _, CancellationToken _) => c.ClientSecret);

            _service = new ClientCredentialTokenService(
                _mockLogger.Object,
                _mockHttpClientFactory.Object,
                _mockConfiguration.Object,
                _mockSecretResolver.Object
            );
        }

        [Fact]
        public async Task GetTokenAsync_SendsTheResolvedSecret_NotThePlaintextField()
        {
            var clientCredentials = new ClientCredential { ItemId = "test-client", ClientSecret = string.Empty, ClientSecretReference = "cs-ref" };
            _mockSecretResolver
                .Setup(r => r.GetSecretAsync(clientCredentials, "test-project-key", It.IsAny<CancellationToken>()))
                .ReturnsAsync("blxk_from-the-store");
            string? sentForm = null;
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => sentForm = req.Content!.ReadAsStringAsync().Result)
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(new TokenResponse { AccessToken = "test-token", ExpiresIn = 60 }))
                });
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));
            _mockConfiguration.Setup(c => c["AuthenticationTokenEndpoint"]).Returns("https://test.api.com/token");

            var result = await _service.GetTokenAsync(clientCredentials, "test-project-key");

            result.Should().Be("test-token");
            sentForm.Should().Contain("client_secret=blxk_from-the-store");
        }

        [Fact]
        public async Task GetTokenAsync_WhenNoSecretCanBeRead_RequestsNoToken()
        {
            var clientCredentials = new ClientCredential { ItemId = "test-client", ClientSecret = string.Empty, ClientSecretReference = "cs-ref" };
            _mockSecretResolver
                .Setup(r => r.GetSecretAsync(clientCredentials, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            var result = await _service.GetTokenAsync(clientCredentials, "test-project-key");

            result.Should().BeNull();
            _mockHttpClientFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
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
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString() == "https://api.blocksdevelopers.com/idp/v1/Authentication/token"),
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
