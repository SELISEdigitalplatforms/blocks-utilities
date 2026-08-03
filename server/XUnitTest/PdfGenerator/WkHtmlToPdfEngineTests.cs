using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PdfSharp.Pdf;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class WkHtmlToPdfEngineTests
    {
        private readonly Mock<ILogger<WkHtmlToPdfEngine>> _loggerMock;
        private readonly Mock<IConfiguration> _configurationMock;

        public WkHtmlToPdfEngineTests()
        {
            _loggerMock = new Mock<ILogger<WkHtmlToPdfEngine>>();
            _configurationMock = new Mock<IConfiguration>();

            _configurationMock
                .Setup(x => x["PdfToolPath"])
                .Returns("fake-path");
        }

        [Fact]
        public async Task UnsupportedMethods_ReturnNull()
        {
            var engine = new WkHtmlToPdfEngine(_loggerMock.Object, _configurationMock.Object);

            Assert.Null(await engine.MergePdfsAsync(new List<Stream>()));
            Assert.Null(await engine.ExtractTextFromPdfAsync(new MemoryStream()));
            Assert.Null(await engine.FixPdfAsync(new MemoryStream()));
            Assert.Null(await engine.StampImageToPdfAsync(new MemoryStream(), new MemoryStream(), new ImageStampOptions()));
            Assert.Null(await engine.StampTextToPdfAsync(new MemoryStream(), new TextStampOptions()));
        }

        [Fact]
        public async Task ConvertHtmlToPdfAsync_ReturnsNull_OnException()
        {
            var engine = new WkHtmlToPdfEngine(_loggerMock.Object, _configurationMock.Object);

            var options = new PdfGenerationOptions
            {
                Profile = new PdfUtilityProfile
                {
                    Zoom = "invalid-number" // Causes float.Parse to throw
                }
            };

            var result = await engine.ConvertHtmlToPdfAsync("<h1>Test</h1>", options);

            Assert.Null(result);
        }

        [Fact]
        public void AddPageNumbers_AddsNumbers_ToPdf()
        {
            // Create simple PDF
            var document = new PdfDocument();
            document.AddPage();
            document.AddPage();

            var stream = new MemoryStream();
            document.Save(stream);
            stream.Position = 0;

            var engine = new WkHtmlToPdfEngine(_loggerMock.Object, _configurationMock.Object);

            var method = typeof(WkHtmlToPdfEngine)
                .GetMethod("AddPageNumbers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var result = (MemoryStream)method!.Invoke(engine, new object[] { stream })!;

            Assert.NotNull(result);
            Assert.True(result.Length > 0);
        }
    }
}
