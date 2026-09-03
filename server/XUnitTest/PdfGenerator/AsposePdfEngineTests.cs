using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.IO.Compression;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    public class AsposePdfEngineTests
    {
        private readonly AsposePdfEngine _engine;

        public AsposePdfEngineTests()
        {
            var logger = Mock.Of<ILogger<AsposePdfEngine>>();
            _engine = new AsposePdfEngine(logger, Mock.Of<IConfiguration>());
        }

        #region MergePdfsAsync

        [Fact]
        public async Task MergePdfsAsync_NullList_ReturnsNull()
        {
            var result = await _engine.MergePdfsAsync(null);
            Assert.Null(result);
        }

        [Fact]
        public async Task MergePdfsAsync_EmptyList_ReturnsNull()
        {
            var result = await _engine.MergePdfsAsync(new List<Stream>());
            Assert.Null(result);
        }

        [Fact]
        public async Task MergePdfsAsync_SinglePdf_ReturnsSameStream()
        {
            var pdf = CreateSimplePdf();

            var result = await _engine.MergePdfsAsync(new List<Stream> { pdf });

            Assert.Same(pdf, result);
        }

        [Fact]
        public async Task MergePdfsAsync_MultiplePdfs_ReturnsMergedPdf()
        {
            var pdf1 = CreateSimplePdf("One");
            var pdf2 = CreateSimplePdf("Two");

            var result = await _engine.MergePdfsAsync(new List<Stream> { pdf1, pdf2 });

            Assert.NotNull(result);
            Assert.True(result.Length > 0);
        }

        #endregion

        #region ExtractTextFromPdfAsync

        [Fact]
        public async Task ExtractTextFromPdfAsync_ValidPdf_ReturnsText()
        {
            var pdf = CreateSimplePdf("Hello World");

            var result = await _engine.ExtractTextFromPdfAsync(pdf);

            Assert.NotNull(result);
            Assert.Contains("Hello", result);
        }

        [Fact]
        public async Task ExtractTextFromPdfAsync_InvalidStream_ReturnsNull()
        {
            var invalid = new MemoryStream(new byte[] { 1, 2, 3 });

            var result = await _engine.ExtractTextFromPdfAsync(invalid);

            Assert.Null(result);
        }

        #endregion

        #region FixPdfAsync

        [Fact]
        public async Task FixPdfAsync_ValidPdf_ReturnsFixedPdf()
        {
            var pdf = CreateSimplePdf("Fix me");

            var result = await _engine.FixPdfAsync(pdf);

            Assert.NotNull(result);
            Assert.True(result.Length > 0);
        }

        [Fact]
        public async Task FixPdfAsync_InvalidPdf_ReturnsNull()
        {
            var invalid = new MemoryStream(new byte[] { 9, 9, 9 });

            var result = await _engine.FixPdfAsync(invalid);

            Assert.Null(result);
        }

        #endregion

        #region StampImageToPdfAsync

        [Fact]
        public async Task StampImageToPdfAsync_ValidInputs_ReturnsPdf()
        {
            var pdf = CreateSimplePdf();
            var image = CreateTestImage();

            var options = new ImageStampOptions
            {
                XPosition = 10,
                YPosition = 10,
                Width = 100,
                Height = 50
            };

            var result = await _engine.StampImageToPdfAsync(pdf, image, options);

            Assert.NotNull(result);
            Assert.True(result.Length > 0);
        }

        [Fact]
        public async Task StampImageToPdfAsync_InvalidPdf_ReturnsNull()
        {
            var invalidPdf = new MemoryStream(new byte[] { 1, 2, 3 });
            var image = CreateTestImage();
            var options = new ImageStampOptions();

            var result = await _engine.StampImageToPdfAsync(invalidPdf, image, options);

            Assert.Null(result);
        }

        #endregion

        #region StampTextToPdfAsync

        [Fact]
        public async Task StampTextToPdfAsync_ValidInputs_ReturnsPdf()
        {
            var pdf = CreateSimplePdf();

            var options = new TextStampOptions
            {
                Text = "STAMP",
                XPosition = 50,
                YPosition = 50
            };

            var result = await _engine.StampTextToPdfAsync(pdf, options);

            Assert.NotNull(result);
            Assert.True(result.Length > 0);
        }

        [Fact]
        public async Task StampTextToPdfAsync_InvalidPdf_ReturnsNull()
        {
            var invalidPdf = new MemoryStream(new byte[] { 1, 2, 3 });
            var options = new TextStampOptions { Text = "X", XPosition = 1, YPosition = 1 };

            var result = await _engine.StampTextToPdfAsync(invalidPdf, options);

            Assert.Null(result);
        }

        #endregion

        #region ConvertHtmlToPdfAsync

        [Fact(Skip = "Aspose HTML to PDF crashes in Linux CI (.NET 9)")]
        public async Task ConvertHtmlToPdfAsync_ValidHtml_ReturnsPdf()
        {
            // Minimal valid HTML and options
            var html = "<html><body><h1>Test</h1></body></html>";
            var options = new PdfGenerationOptions();

            var result = await _engine.ConvertHtmlToPdfAsync(html, options);

            Assert.NotNull(result);
            Assert.True(result.Length > 0);
        }

        [Fact]
        public async Task ConvertHtmlToPdfAsync_NullHtml_ReturnsNull()
        {
            var result = await _engine.ConvertHtmlToPdfAsync(null, null);
            Assert.Null(result);
        }

        #endregion

        #region Helpers

        private static Stream CreateSimplePdf(string text = "Hello")
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString(text, new XFont("Arial", 12), XBrushes.Black, 20, 40);

            var ms = new MemoryStream();
            doc.Save(ms);
            ms.Position = 0;
            return ms;
        }

        private static Stream CreateTestImage()
        {
            return TestPdfFactory.CreatePngImage(50, 50);
        }

        #endregion
    }
}
