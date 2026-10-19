using System.Net;
using DomainService.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Utility.DomainService.MagicLink.Service;
using Utility.DomainService.MagicLink.Utilities;
using Utility.DomainService.Shared.Utilities;
using MagicLinkEntity = Utility.DomainService.MagicLink.Models.MagicLink;

namespace XUnitTest.MagicLink;

/// <summary>
/// Where an Action link's request, and the client-credential token attached to it, may go.
/// </summary>
/// <remarks>
/// Guards against two customer-visible failures. A link creator who picks the target URL could
/// have the Worker deliver a service token holding the credential's roles to their own server.
/// They could also aim the Worker at cluster-internal addresses or the cloud metadata endpoint
/// (SSRF), reading responses the internet was never meant to see.
/// </remarks>
public sealed class MagicLinkActionTargetTests
{
    private const string TokenEndpoint = "https://api.blocks.test/idp/v1/Authentication/token";

    private readonly Mock<IHttpClientFactory> _clients = new();
    private readonly MagicLinkActionExecutor _executor;

    public MagicLinkActionTargetTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AuthenticationTokenEndpoint"] = TokenEndpoint })
            .Build();

        _executor = new MagicLinkActionExecutor(Mock.Of<ILogger<MagicLinkActionExecutor>>(), _clients.Object, configuration);
    }

    [Theory]
    [InlineData("https://api.blocks.test/iam/v1/User/Update")]
    [InlineData("https://API.BLOCKS.TEST/anything")]
    public void A_token_may_go_to_the_host_that_issued_it(string target)
    {
        _executor.MayReceiveToken(Link(target)).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://attacker.example/collect")]
    [InlineData("http://api.blocks.test/iam/v1/User/Update")]
    [InlineData("https://api.blocks.test:8443/iam/v1/User/Update")]
    [InlineData("https://api.blocks.test.attacker.example/")]
    [InlineData("not a url")]
    public void A_token_may_not_go_anywhere_else(string target)
    {
        _executor.MayReceiveToken(Link(target)).Should().BeFalse(
            "a different scheme, port or host is a different server, and it would receive the credential's roles");
    }

    [Fact]
    public async Task A_token_bearing_action_aimed_elsewhere_is_never_sent()
    {
        var handler = new Mock<HttpMessageHandler>();
        _clients.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));

        var result = await _executor.ExecuteActionAsync(Link("https://attacker.example/collect"), "service-token");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        handler.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Stored_headers_cannot_reroute_the_request_or_replace_the_token()
    {
        HttpRequestMessage? sent = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        _clients.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));

        var link = Link("https://api.blocks.test/iam/v1/User/Update");
        link.RequestHeaders = "{\"Host\":\"attacker.example\",\"Authorization\":\"Bearer stolen\",\"X-Trace\":\"1\"}";

        await _executor.ExecuteActionAsync(link, "service-token");

        sent!.Headers.Authorization!.Parameter.Should().Be("service-token");
        sent.Headers.Host.Should().BeNull("a Host header would route the token to another site behind the gateway");
        sent.Headers.GetValues("X-Trace").Should().Contain("1", "ordinary headers still pass through");
    }

    [Fact]
    public async Task Actions_use_the_client_registered_for_them()
    {
        _clients.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(Mock.Of<HttpMessageHandler>()));

        await _executor.ExecuteActionAsync(Link("https://example.com/hook"));

        _clients.Verify(f => f.CreateClient(MagicLinkConstants.ActionHttpClientName), Times.Once,
            "only that client refuses private addresses; the default one would connect anywhere");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void Internal_addresses_are_not_public(string address)
    {
        PublicNetwork.IsPublic(IPAddress.Parse(address)).Should().BeFalse();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Internet_addresses_are_public(string address)
    {
        PublicNetwork.IsPublic(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://localhost:9/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task The_registered_action_client_refuses_to_connect_to_internal_addresses(string target)
    {
        var services = new ServiceCollection();
        services.RegisterUtilityServices();
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(MagicLinkConstants.ActionHttpClientName);

        var call = () => client.GetAsync(new Uri(target));

        (await call.Should().ThrowAsync<HttpRequestException>())
            .Which.ToString().Should().Contain("does not resolve to a public address",
                "the connection must be refused by the guard, after DNS, not merely fail to connect");
    }

    private static MagicLinkEntity Link(string uri) => new()
    {
        ItemId = "AbCdEf",
        RequestMethod = "GET",
        Uri = uri
    };
}
