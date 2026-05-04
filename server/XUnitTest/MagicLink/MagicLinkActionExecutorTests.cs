using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Utility.DomainService.MagicLink.Service;

namespace XUnitTest.MagicLink
{
    public class MagicLinkActionExecutorTests
    {
        private readonly Mock<ILogger<MagicLinkActionExecutor>> _mockLogger;
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly MagicLinkActionExecutor _executor;

        public MagicLinkActionExecutorTests()
        {
            _mockLogger = new Mock<ILogger<MagicLinkActionExecutor>>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _executor = new MagicLinkActionExecutor(_mockLogger.Object, _mockHttpClientFactory.Object);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        public async Task ExecuteActionAsync_ShouldExecuteSuccessfully_ForAllHttpMethods(string method)
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = method,
                Uri = "https://api.test.com/endpoint",
                RequestPayload = method is "POST" or "PUT" ? "{\"data\":\"test\"}" : null,
                RequestEncodedQueryString = "param1=value1",
                RequestHeaders = "{\"X-Custom-Header\":\"CustomValue\"}"
            };

            var responseData = new { message = "success" };
            var mockHandler = CreateMockHandler(HttpStatusCode.OK, JsonSerializer.Serialize(responseData));
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            var result = await _executor.ExecuteActionAsync(link, "test-token");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.StatusCode.Should().Be(200);
            result.Data.Should().NotBeNull();
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldReturnError_WhenRequestMethodIsNull()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = null,
                Uri = "https://api.test.com/endpoint"
            };

            // Act
            var result = await _executor.ExecuteActionAsync(link);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.StatusCode.Should().Be(400);
            result.ErrorMessage.Should().Contain("RequestMethod is required");
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldReturnError_WhenRequestMethodIsEmpty()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "",
                Uri = "https://api.test.com/endpoint"
            };

            // Act
            var result = await _executor.ExecuteActionAsync(link);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldReturnError_WhenMethodIsUnsupported()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "PATCH",
                Uri = "https://api.test.com/endpoint"
            };

            // Act
            var result = await _executor.ExecuteActionAsync(link);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.StatusCode.Should().Be(400);
            result.ErrorMessage.Should().Contain("Unsupported HTTP method");
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldHandleException_AndReturnError()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "GET",
                Uri = "https://api.test.com/endpoint"
            };

            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            var result = await _executor.ExecuteActionAsync(link);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.StatusCode.Should().Be(500);
            result.ErrorMessage.Should().Contain("Error executing action");
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldBuildUrlWithQueryString()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "GET",
                Uri = "https://api.test.com/endpoint?existing=param",
                RequestEncodedQueryString = "new=param"
            };

            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("{}") });

            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            await _executor.ExecuteActionAsync(link);

            // Assert
            capturedRequest.Should().NotBeNull();
            capturedRequest!.RequestUri!.ToString().Should().Contain("existing=param&new=param");
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldSetAuthorizationHeader_WhenTokenProvided()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "GET",
                Uri = "https://api.test.com/endpoint"
            };

            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("{}") });

            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            await _executor.ExecuteActionAsync(link, "my-bearer-token");

            // Assert
            capturedRequest.Should().NotBeNull();
            capturedRequest!.Headers.Authorization.Should().NotBeNull();
            capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
            capturedRequest.Headers.Authorization.Parameter.Should().Be("my-bearer-token");
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldAddCustomHeaders_WhenRequestHeadersProvided()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "GET",
                Uri = "https://api.test.com/endpoint",
                RequestHeaders = "{\"X-Custom-Header\":\"CustomValue\",\"X-Another-Header\":\"AnotherValue\"}"
            };

            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("{}") });

            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            await _executor.ExecuteActionAsync(link);

            // Assert
            capturedRequest.Should().NotBeNull();
            capturedRequest!.Headers.GetValues("X-Custom-Header").Should().Contain("CustomValue");
            capturedRequest.Headers.GetValues("X-Another-Header").Should().Contain("AnotherValue");
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldHandleInvalidHeadersJson_AndContinueExecution()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "GET",
                Uri = "https://api.test.com/endpoint",
                RequestHeaders = "invalid-json"
            };

            var mockHandler = CreateMockHandler(HttpStatusCode.OK, "{}");
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            var result = await _executor.ExecuteActionAsync(link);

            // Assert - Should succeed despite invalid headers JSON
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldHandleNonSuccessStatusCode()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "GET",
                Uri = "https://api.test.com/endpoint"
            };

            var mockHandler = CreateMockHandler(HttpStatusCode.NotFound, "Not found");
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            var result = await _executor.ExecuteActionAsync(link);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.StatusCode.Should().Be(404);
            result.ErrorMessage.Should().Contain("HTTP 404");
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldHandleInvalidJsonResponse()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "GET",
                Uri = "https://api.test.com/endpoint"
            };

            var mockHandler = CreateMockHandler(HttpStatusCode.OK, "invalid json response");
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            var result = await _executor.ExecuteActionAsync(link);

            // Assert - Should succeed but Data should be null due to deserialization error
            result.IsSuccess.Should().BeTrue();
            result.StatusCode.Should().Be(200);
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldHandleEmptyResponseContent()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "DELETE",
                Uri = "https://api.test.com/endpoint"
            };

            var mockHandler = CreateMockHandler(HttpStatusCode.NoContent, "");
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            var result = await _executor.ExecuteActionAsync(link);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.StatusCode.Should().Be(204);
            result.Data.Should().BeNull();
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldSendPayload_ForPostRequest()
        {
            // Arrange
            var payload = "{\"name\":\"test\",\"value\":123}";
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "POST",
                Uri = "https://api.test.com/endpoint",
                RequestPayload = payload
            };

            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("{}") });

            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            await _executor.ExecuteActionAsync(link);

            // Assert
            capturedRequest.Should().NotBeNull();
            var content = await capturedRequest!.Content!.ReadAsStringAsync();
            content.Should().Be(payload);
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldNotSendPayload_WhenPayloadIsNull()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "POST",
                Uri = "https://api.test.com/endpoint",
                RequestPayload = null
            };

            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("{}") });

            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            await _executor.ExecuteActionAsync(link);

            // Assert
            capturedRequest.Should().NotBeNull();
            capturedRequest!.Content.Should().BeNull();
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldHandleUrlWithoutQueryString()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "GET",
                Uri = "https://api.test.com/endpoint",
                RequestEncodedQueryString = "param1=value1"
            };

            HttpRequestMessage? capturedRequest = null;
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("{}") });

            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            await _executor.ExecuteActionAsync(link);

            // Assert
            capturedRequest.Should().NotBeNull();
            capturedRequest!.RequestUri!.ToString().Should().EndWith("?param1=value1");
        }

        [Fact]
        public async Task ExecuteActionAsync_ShouldSkipEmptyHeaders()
        {
            // Arrange
            var link = new Utility.DomainService.MagicLink.Models.MagicLink
            {
                ItemId = "test-link",
                RequestMethod = "GET",
                Uri = "https://api.test.com/endpoint",
                RequestHeaders = "{\"\":\"value\",\"key\":\"\"}"
            };

            var mockHandler = CreateMockHandler(HttpStatusCode.OK, "{}");
            _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler.Object));

            // Act
            var result = await _executor.ExecuteActionAsync(link);

            // Assert - Should succeed even with empty header keys/values
            result.IsSuccess.Should().BeTrue();
        }

        private Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, string content)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(content)
                });
            return mockHandler;
        }
    }
}
