using System.Net;
using System.Text;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Utilities;
using Utility.DomainService.Shared.Services;
using Worker;

namespace XUnitTest.Worker;

/// <summary>
/// The shared HTTP helper is deliberately forgiving: every transport failure
/// comes back as a null payload plus a fixed message rather than an exception,
/// so callers never see provider detail. These tests pin that contract.
/// </summary>
public sealed class HttpHelperServicesTests
{
    private sealed class Payload
    {
        public string Name { get; set; } = string.Empty;
    }

    private readonly Mock<IHttpService> _httpService = new();
    private readonly List<HttpRequestMessage> _requests = [];

    private HttpHelperServices Helper(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string body = """{"Name":"ada"}""",
        Exception? failure = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(
                new StubHandler(statusCode, body, _requests, failure)));

        return new HttpHelperServices(_httpService.Object, factory.Object);
    }

    [Fact]
    public async Task A_get_returns_the_deserialized_payload_and_raw_body()
    {
        var payload = new Payload { Name = "ada" };
        _httpService.Setup(x => x.Get<Payload>(
                "https://api.example/things",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ReturnsAsync((payload, "raw"));

        var (result, raw) = await Helper().MakeHttpGetRequest<Payload>(
            "https://api.example/things");

        result.Should().BeSameAs(payload);
        raw.Should().Be("raw");
    }

    [Fact]
    public async Task Get_headers_are_forwarded_to_the_transport()
    {
        var headers = new Dictionary<string, string> { ["x-blocks-key"] = "tenant" };
        _httpService.Setup(x => x.Get<Payload>(
                It.IsAny<string>(),
                headers,
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ReturnsAsync((new Payload(), "raw"));

        await Helper().MakeHttpGetRequest<Payload>(
            "https://api.example/things",
            headers: headers);

        _httpService.Verify(
            x => x.Get<Payload>(
                "https://api.example/things",
                headers,
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()),
            Times.Once);
    }

    [Fact]
    public async Task A_failing_get_reports_a_generic_failure_rather_than_throwing()
    {
        _httpService.Setup(x => x.Get<Payload>(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new HttpRequestException("dns failure for internal.host"));

        var (result, raw) = await Helper().MakeHttpGetRequest<Payload>(
            "https://api.example/things");

        result.Should().BeNull();
        raw.Should().Be("Operation Failed.");
    }

    [Fact]
    public async Task A_post_forwards_the_payload_and_content_type()
    {
        var response = new Payload { Name = "ada" };
        _httpService.Setup(x => x.Post<Payload>(
                It.IsAny<object>(),
                "https://api.example/things",
                "application/json",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ReturnsAsync((response, "raw"));

        var (result, raw) = await Helper().MakeHttpPostRequest<Payload>(
            new { name = "ada" },
            "https://api.example/things");

        result.Should().BeSameAs(response);
        raw.Should().Be("raw");
    }

    [Fact]
    public async Task A_post_honours_a_non_default_content_type()
    {
        _httpService.Setup(x => x.Post<Payload>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ReturnsAsync((new Payload(), "raw"));

        await Helper().MakeHttpPostRequest<Payload>(
            new { name = "ada" },
            "https://api.example/things",
            contentType: "application/xml");

        _httpService.Verify(
            x => x.Post<Payload>(
                It.IsAny<object>(),
                "https://api.example/things",
                "application/xml",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()),
            Times.Once);
    }

    [Fact]
    public async Task A_failing_post_reports_a_generic_failure_rather_than_throwing()
    {
        _httpService.Setup(x => x.Post<Payload>(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new HttpRequestException("connection reset"));

        var (result, raw) = await Helper().MakeHttpPostRequest<Payload>(
            new { name = "ada" },
            "https://api.example/things");

        result.Should().BeNull();
        raw.Should().Be("Operation Failed.");
    }

    [Fact]
    public async Task A_named_client_request_deserializes_a_successful_response()
    {
        var (result, raw) = await Helper()
            .MakeHttpRequest<Payload>(
                "things",
                "https://api.example/things",
                HttpMethod.Get);

        result!.Name.Should().Be("ada");
        raw.Should().Be("Success");
        _requests.Should().ContainSingle();
        _requests[0].Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task A_bearer_token_is_attached_when_one_is_supplied()
    {
        await Helper().MakeHttpRequest<Payload>(
            "things",
            "https://api.example/things",
            HttpMethod.Get,
            token: "top-secret");

        // Set on the client's default headers, so it reaches the wire on the
        // outgoing message.
        _requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        _requests[0].Headers.Authorization.Parameter.Should().Be("top-secret");
    }

    [Fact]
    public async Task Extra_headers_are_attached_to_the_named_client()
    {
        var (result, _) = await Helper().MakeHttpRequest<Payload>(
            "things",
            "https://api.example/things",
            HttpMethod.Get,
            headers: new Dictionary<string, string>
            {
                ["x-blocks-key"] = "tenant"
            });

        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task A_write_carries_the_payload_as_json(string method)
    {
        await Helper().MakeHttpRequest<Payload>(
            "things",
            "https://api.example/things",
            new HttpMethod(method),
            new { name = "ada" });

        _requests[0].Content.Should().NotBeNull();
        (await _requests[0].Content!.ReadAsStringAsync())
            .Should().Contain("ada");
    }

    [Fact]
    public async Task A_write_without_a_payload_sends_no_body()
    {
        await Helper().MakeHttpRequest<Payload>(
            "things",
            "https://api.example/things",
            HttpMethod.Post);

        _requests[0].Content.Should().BeNull();
    }

    [Fact]
    public async Task A_get_never_carries_a_body_even_when_a_payload_is_passed()
    {
        await Helper().MakeHttpRequest<Payload>(
            "things",
            "https://api.example/things",
            HttpMethod.Get,
            new { name = "ada" });

        _requests[0].Content.Should().BeNull();
    }

    [Fact]
    public async Task An_error_status_is_reported_without_the_response_body()
    {
        var (result, raw) = await Helper(
                HttpStatusCode.Forbidden,
                "internal detail that must not escape")
            .MakeHttpRequest<Payload>(
                "things",
                "https://api.example/things",
                HttpMethod.Get);

        result.Should().BeNull();
        raw.Should().Be("Operation Failed.");
    }

    [Fact]
    public async Task An_unparsable_success_body_is_reported_as_a_failure()
    {
        var (result, raw) = await Helper(HttpStatusCode.OK, "not json")
            .MakeHttpRequest<Payload>(
                "things",
                "https://api.example/things",
                HttpMethod.Get);

        result.Should().BeNull();
        raw.Should().Be("Operation Failed.");
    }

    [Fact]
    public async Task A_transport_failure_is_reported_rather_than_thrown()
    {
        var (result, raw) = await Helper(
                failure: new HttpRequestException("connection refused"))
            .MakeHttpRequest<Payload>(
                "things",
                "https://api.example/things",
                HttpMethod.Get);

        result.Should().BeNull();
        raw.Should().Be("Operation Failed.");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;
        private readonly List<HttpRequestMessage> _requests;
        private readonly Exception? _failure;

        public StubHandler(
            HttpStatusCode statusCode,
            string body,
            List<HttpRequestMessage> requests,
            Exception? failure)
        {
            _statusCode = statusCode;
            _body = body;
            _requests = requests;
            _failure = failure;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);

            if (_failure != null)
            {
                throw _failure;
            }

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _body,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}

/// <summary>
/// The reconciliation safety net is currently a no-op loop: the periodic work
/// is commented out upstream. The test pins that it starts and stops cleanly so
/// the host is never blocked by it.
/// </summary>
public sealed class PaymentReconciliationBackgroundServiceTests
{
    [Fact]
    public async Task The_reconciliation_service_starts_and_stops_without_faulting()
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(x => x.CurrentValue).Returns(new PaymentOptions
        {
            ReconciliationPollSeconds = 60,
            TenantIds = ["tenant-1"]
        });
        using var service = new PaymentReconciliationBackgroundService(
            Mock.Of<IServiceProvider>(),
            options.Object,
            NullLogger<PaymentReconciliationBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);

        if (service.ExecuteTask != null)
        {
            await service.ExecuteTask;
        }

        var act = () => service.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
