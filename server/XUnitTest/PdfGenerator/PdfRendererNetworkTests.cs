using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PuppeteerSharp;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.Shared.Utilities;

namespace XUnitTest.PdfGenerator;

/// <summary>
/// What the PDF renderers may fetch while they render caller-supplied HTML and documents.
/// </summary>
/// <remarks>
/// Guards against SSRF through the PDF endpoints: an <c>&lt;img&gt;</c>, stylesheet or script URL
/// in the submitted HTML pointing at the cloud metadata endpoint, a cluster-internal service or a
/// local file, whose response would then be rendered into a PDF the caller downloads.
/// </remarks>
public sealed class PdfRendererNetworkTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/logo.png")]
    [InlineData("/etc/passwd")]
    [InlineData("http://127.0.0.1:9/logo.png")]
    [InlineData("http://localhost:9/logo.png")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task Only_public_http_resources_are_fetched(string? location)
    {
        var resource = await PublicNetwork.FetchAsync(location, CancellationToken.None);

        resource.Should().BeNull("a renderer must never receive bytes from a local file or an internal address");
    }

    [Fact]
    public async Task Inline_data_urls_load_normally()
    {
        var request = Request("data:image/png;base64,iVBORw0KGgo=");

        await PuppeteerSharpEngine.RouteRequestAsync(request.Object);

        request.Verify(r => r.ContinueAsync(It.IsAny<Payload>(), It.IsAny<int?>()), Times.Once,
            "templates embed their logos as data URIs and must keep rendering");
    }

    [Theory]
    [InlineData("GET", "http://169.254.169.254/latest/meta-data/")]
    [InlineData("GET", "file:///etc/passwd")]
    [InlineData("POST", "https://example.com/collect")]
    public async Task Chromium_requests_that_are_not_public_gets_are_aborted(string method, string location)
    {
        var request = Request(location, new HttpMethod(method));

        await PuppeteerSharpEngine.RouteRequestAsync(request.Object);

        request.Verify(r => r.AbortAsync(It.IsAny<RequestAbortErrorCode>(), It.IsAny<int?>()), Times.Once);
        request.Verify(r => r.ContinueAsync(It.IsAny<Payload>(), It.IsAny<int?>()), Times.Never,
            "continuing would let Chromium open the connection itself");
        request.Verify(r => r.RespondAsync(It.IsAny<ResponseData>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task Chromium_pages_intercept_every_request()
    {
        var page = new Mock<IPage>();
        page.Setup(p => p.PdfDataAsync(It.IsAny<PdfOptions>())).ReturnsAsync([1]);
        var browser = new Mock<IBrowser>();
        browser.Setup(b => b.IsConnected).Returns(true);
        browser.Setup(b => b.NewPageAsync()).ReturnsAsync(page.Object);

        await using var engine = new PuppeteerSharpEngine(Mock.Of<ILogger<PuppeteerSharpEngine>>(), Mock.Of<IConfiguration>());
        typeof(PuppeteerSharpEngine)
            .GetField("_browser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(engine, browser.Object);

        await engine.ConvertHtmlToPdfAsync("<img src=\"http://169.254.169.254/\">", new PdfGenerationOptions());

        page.Verify(p => p.SetRequestInterceptionAsync(true), Times.Once,
            "without interception Chromium fetches whatever the HTML names");
    }

    [Fact]
    public async Task Aspose_never_connects_to_an_internal_address_named_in_a_document()
    {
        using var internalService = new ConnectionCounter();
        var html = $"<html><body><img src=\"{internalService.Url}logo.png\"><p>Invoice</p></body></html>";
        var converter = new AsposeDocumentToPdfConverter(
            Mock.Of<ILogger<AsposeDocumentToPdfConverter>>(), new ConfigurationBuilder().Build());

        using var pdf = await converter.ConvertToPdfAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(html)), new DocumentConversionOptions());

        pdf.Should().NotBeNull("an unreachable image is skipped, not a reason to fail the document");
        internalService.Connections.Should().Be(0);
    }

    [Fact]
    public async Task Aspose_never_connects_to_an_internal_address_named_in_html_or_its_header()
    {
        using var internalService = new ConnectionCounter();
        var engine = new AsposePdfEngine(Mock.Of<ILogger<AsposePdfEngine>>(), new ConfigurationBuilder().Build());

        using var pdf = await engine.ConvertHtmlToPdfAsync(
            $"<p>Body</p><img src=\"{internalService.Url}body.png\">",
            new PdfGenerationOptions { HeaderHtml = $"<img src=\"{internalService.Url}header.png\">" });

        pdf.Should().NotBeNull();
        internalService.Connections.Should().Be(0, "the header is inserted as HTML too and must follow the same rule");
    }

    private static Mock<IRequest> Request(string location, HttpMethod? method = null)
    {
        var request = new Mock<IRequest>();
        request.Setup(r => r.Url).Returns(location);
        request.Setup(r => r.Method).Returns(method ?? HttpMethod.Get);
        return request;
    }

    /// <summary>A loopback "internal service" that counts the connections it receives.</summary>
    private sealed class ConnectionCounter : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private int _connections;

        public ConnectionCounter()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

        public int Connections => Volatile.Read(ref _connections);

        public void Dispose() => _listener.Dispose();

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _connections);
                }
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // Stopped
            }
        }
    }
}
