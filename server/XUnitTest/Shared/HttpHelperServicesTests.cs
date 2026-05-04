using System.Net;
using System.Text.Json;
using Blocks.Genesis;
using DomainService.Shared.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Utility.DomainService.Shared.Services;

namespace XUnitTest.Shared
{
    public class HttpHelperServicesTests
    {
        private readonly Mock<IHttpService> _httpServiceMock;
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
        private readonly HttpHelperServices _service;

        public HttpHelperServicesTests()
        {
            _httpServiceMock = new Mock<IHttpService>();
            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _service = new HttpHelperServices(_httpServiceMock.Object, _httpClientFactoryMock.Object);
        }

        [Fact]
        public async Task MakeHttpGetRequest_ShouldReturnData_WhenRequestSucceeds()
        {
            var headers = new Dictionary<string, string> { ["x-project"] = "demo" };
            var expectedData = new SampleResponse { Message = "ok" };

            _httpServiceMock
                .Setup(x => x.Get<SampleResponse>("https://test.com", headers, It.IsAny<CancellationToken>()))
                .ReturnsAsync((expectedData, "raw-response"));

            var (data, response) = await _service.MakeHttpGetRequest<SampleResponse>("https://test.com", headers: headers);

            data.Should().BeEquivalentTo(expectedData);
            response.Should().Be("raw-response");
        }

        [Fact]
        public async Task MakeHttpGetRequest_ShouldReturnFailure_WhenExceptionIsThrown()
        {
            _httpServiceMock
                .Setup(x => x.Get<SampleResponse>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("network error"));

            var (data, response) = await _service.MakeHttpGetRequest<SampleResponse>("https://test.com");

            data.Should().BeNull();
            response.Should().Be("Operation Failed.");
        }

        [Fact]
        public async Task MakeHttpPostRequest_ShouldReturnData_WhenRequestSucceeds()
        {
            var payload = new { Name = "copilot" };
            var headers = new Dictionary<string, string> { ["x-project"] = "demo" };
            var expectedData = new SampleResponse { Message = "created" };

            _httpServiceMock
                .Setup(x => x.Post<SampleResponse>(payload, "https://test.com", "application/json", headers, It.IsAny<CancellationToken>()))
                .ReturnsAsync((expectedData, "post-raw"));

            var (data, response) = await _service.MakeHttpPostRequest<SampleResponse>(payload, "https://test.com", headers);

            data.Should().BeEquivalentTo(expectedData);
            response.Should().Be("post-raw");
        }

        [Fact]
        public async Task MakeHttpPostRequest_ShouldReturnFailure_WhenExceptionIsThrown()
        {
            var payload = new { Name = "copilot" };

            _httpServiceMock
                .Setup(x => x.Post<SampleResponse>(It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("post error"));

            var (data, response) = await _service.MakeHttpPostRequest<SampleResponse>(payload, "https://test.com");

            data.Should().BeNull();
            response.Should().Be("Operation Failed.");
        }

        [Fact]
        public async Task MakeHttpRequest_ShouldReturnSuccess_AndApplyTokenHeadersAndPayload()
        {
            HttpRequestMessage? capturedRequest = null;
            var handler = new Mock<HttpMessageHandler>();

            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, _) => capturedRequest = request)
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(new SampleResponse { Message = "ok" }))
                });

            _httpClientFactoryMock
                .Setup(x => x.CreateClient("test-client"))
                .Returns(new HttpClient(handler.Object));

            var headers = new Dictionary<string, string> { ["x-project"] = "demo" };
            var payload = new { Name = "copilot" };

            var (data, response) = await _service.MakeHttpRequest<SampleResponse>(
                "test-client",
                "https://test.com/resource",
                HttpMethod.Post,
                payload,
                headers,
                "token-123");

            response.Should().Be("Success");
            data.Should().NotBeNull();
            data!.Message.Should().Be("ok");

            capturedRequest.Should().NotBeNull();
            capturedRequest!.Headers.Authorization.Should().NotBeNull();
            capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
            capturedRequest.Headers.Authorization.Parameter.Should().Be("token-123");
            capturedRequest.Headers.Contains("x-project").Should().BeTrue();
            (await capturedRequest.Content!.ReadAsStringAsync()).Should().Contain("copilot");
        }

        [Fact]
        public async Task MakeHttpRequest_ShouldReturnFailure_WhenResponseStatusIsNotSuccess()
        {
            var handler = new Mock<HttpMessageHandler>();

            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    Content = new StringContent("invalid")
                });

            _httpClientFactoryMock
                .Setup(x => x.CreateClient("test-client"))
                .Returns(new HttpClient(handler.Object));

            var (data, response) = await _service.MakeHttpRequest<SampleResponse>(
                "test-client",
                "https://test.com/resource",
                HttpMethod.Get);

            data.Should().BeNull();
            response.Should().Be("Operation Failed.");
        }

        [Fact]
        public async Task MakeHttpRequest_ShouldReturnFailure_WhenExceptionIsThrown()
        {
            var handler = new Mock<HttpMessageHandler>();

            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("network error"));

            _httpClientFactoryMock
                .Setup(x => x.CreateClient("test-client"))
                .Returns(new HttpClient(handler.Object));

            var (data, response) = await _service.MakeHttpRequest<SampleResponse>(
                "test-client",
                "https://test.com/resource",
                HttpMethod.Put,
                new { Name = "ignored-on-failure" });

            data.Should().BeNull();
            response.Should().Be("Operation Failed.");
        }

        private sealed class SampleResponse
        {
            public string? Message { get; set; }
        }
    }
}
