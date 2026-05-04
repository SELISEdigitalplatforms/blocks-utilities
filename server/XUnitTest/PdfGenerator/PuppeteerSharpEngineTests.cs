using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PuppeteerSharp;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class PuppeteerSharpEngineTests
    {
        private static void InjectBrowser(PuppeteerSharpEngine engine, IBrowser browser)
        {
            var field = typeof(PuppeteerSharpEngine)
                .GetField("_browser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            field!.SetValue(engine, browser);
        }

        private readonly Mock<ILogger<PuppeteerSharpEngine>> _loggerMock;
        private readonly Mock<IConfiguration> _configurationMock;

        public PuppeteerSharpEngineTests()
        {
            _loggerMock = new Mock<ILogger<PuppeteerSharpEngine>>();
            _configurationMock = new Mock<IConfiguration>();

            _configurationMock
                .Setup(x => x["PuppeteerSharp:ExecutablePath"])
                .Returns("fake-path");
        }

        [Fact]
        public async Task ConvertHtmlToPdfAsync_ReturnsStream_WhenSuccessful()
        {
            var browserMock = new Mock<IBrowser>();
            var pageMock = new Mock<IPage>();

            browserMock.Setup(b => b.IsConnected).Returns(true);
            browserMock.Setup(b => b.NewPageAsync()).ReturnsAsync(pageMock.Object);

            pageMock.Setup(p => p.SetContentAsync(It.IsAny<string>(), It.IsAny<NavigationOptions>()))
                .Returns(Task.CompletedTask);

            pageMock.Setup(p => p.PdfDataAsync(It.IsAny<PdfOptions>()))
                .ReturnsAsync(Encoding.UTF8.GetBytes("fake-pdf"));

            var engine = new PuppeteerSharpEngine(_loggerMock.Object, _configurationMock.Object);

            InjectBrowser(engine, browserMock.Object);

            var result = await engine.ConvertHtmlToPdfAsync("<h1>Hello</h1>", new PdfGenerationOptions());

            Assert.NotNull(result);
            Assert.True(result!.Length > 0);
        }

        [Fact]
        public async Task ConvertHtmlToPdfAsync_ReturnsNull_OnException()
        {
            var browserMock = new Mock<IBrowser>();
            browserMock.Setup(b => b.IsConnected).Returns(true);
            browserMock.Setup(b => b.NewPageAsync())
                .ThrowsAsync(new Exception("browser error"));

            var engine = new PuppeteerSharpEngine(_loggerMock.Object, _configurationMock.Object);
            InjectBrowser(engine, browserMock.Object);

            var result = await engine.ConvertHtmlToPdfAsync("<h1>Fail</h1>", new PdfGenerationOptions());

            Assert.Null(result);
        }

        [Fact]
        public async Task UnsupportedMethods_ReturnNull()
        {
            var engine = new PuppeteerSharpEngine(_loggerMock.Object, _configurationMock.Object);

            Assert.Null(await engine.MergePdfsAsync(new List<Stream>()));
            Assert.Null(await engine.FixPdfAsync(new MemoryStream()));
            Assert.Null(await engine.ExtractTextFromPdfAsync(new MemoryStream()));
            Assert.Null(await engine.StampImageToPdfAsync(new MemoryStream(), new MemoryStream(), new ImageStampOptions()));
            Assert.Null(await engine.StampTextToPdfAsync(new MemoryStream(), new TextStampOptions()));
        }

        [Fact]
        public async Task DisposeAsync_ClosesBrowser()
        {
            var browserMock = new Mock<IBrowser>();

            browserMock.Setup(b => b.CloseAsync()).Returns(Task.CompletedTask);
            browserMock.Setup(b => b.DisposeAsync()).Returns(ValueTask.CompletedTask);

            var engine = new PuppeteerSharpEngine(_loggerMock.Object, _configurationMock.Object);
            InjectBrowser(engine, browserMock.Object);

            await engine.DisposeAsync();

            browserMock.Verify(b => b.CloseAsync(), Times.Once);
            browserMock.Verify(b => b.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task ConvertHtmlToPdfAsync_AppliesLandscape_FromProfile()
        {
            PdfOptions? capturedOptions = null;

            var browserMock = new Mock<IBrowser>();
            var pageMock = new Mock<IPage>();

            browserMock.Setup(b => b.IsConnected).Returns(true);
            browserMock.Setup(b => b.NewPageAsync()).ReturnsAsync(pageMock.Object);

            pageMock.Setup(p => p.SetContentAsync(It.IsAny<string>(), It.IsAny<NavigationOptions>()))
                .Returns(Task.CompletedTask);

            pageMock.Setup(p => p.PdfDataAsync(It.IsAny<PdfOptions>()))
                .Callback<PdfOptions>(opt => capturedOptions = opt)
                .ReturnsAsync(new byte[] { 1, 2 });

            var engine = new PuppeteerSharpEngine(_loggerMock.Object, _configurationMock.Object);
            InjectBrowser(engine, browserMock.Object);

            var options = new PdfGenerationOptions
            {
                Profile = new PdfUtilityProfile
                {
                    Orientation = "landscape"
                }
            };

            await engine.ConvertHtmlToPdfAsync("<h1>Test</h1>", options);

            Assert.True(capturedOptions!.Landscape);
        }




    }
}
