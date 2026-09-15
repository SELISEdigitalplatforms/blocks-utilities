using DomainService.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StorageDriver;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.PdfGenerator.Utilities;

namespace XUnitTest.PdfGenerator
{
    public class SinglePdfGeneratorServiceTests
    {
        private readonly Mock<PuppeteerSharpEngine> _engineMock;
        private readonly Mock<PdfStorageHelper> _storageMock;
        private readonly Mock<ILogger<SinglePdfGeneratorService>> _loggerMock;

        public SinglePdfGeneratorServiceTests()
        {
            _engineMock = new Mock<PuppeteerSharpEngine>(
                Mock.Of<ILogger<PuppeteerSharpEngine>>(),
                Mock.Of<IConfiguration>());

            _storageMock = new Mock<PdfStorageHelper>(
                Mock.Of<ILogger<PdfStorageHelper>>(),
                Mock.Of<IStorageDriverService>(),
                Mock.Of<IHttpClientFactory>());

            _loggerMock = new Mock<ILogger<SinglePdfGeneratorService>>();
        }

        private SinglePdfGeneratorService CreateSut(TimeSpan? timeout = null)
        {
            return new SinglePdfGeneratorService(_engineMock.Object, _storageMock.Object, _loggerMock.Object)
            {
                RenderTimeout = timeout ?? SinglePdfGeneratorService.DefaultRenderTimeout
            };
        }

        private static CreateSinglePdfFromHtmlRequest ValidRequest()
        {
            return new CreateSinglePdfFromHtmlRequest
            {
                ProjectKey = "proj_1",
                HtmlContent = "<html><body>Invoice #123</body></html>",
                OutputFileName = "invoice-123.pdf"
            };
        }

        private void SetupSuccessfulRender(byte[]? pdfBytes = null)
        {
            var bytes = pdfBytes ?? [0x25, 0x50, 0x44, 0x46]; // %PDF

            _engineMock
                .Setup(e => e.ConvertHtmlToPdfAsync(It.IsAny<string>(), It.IsAny<PdfGenerationOptions>()))
                .ReturnsAsync(() => new MemoryStream(bytes));
        }

        private void SetupSuccessfulStorage()
        {
            _storageMock
                .Setup(s => s.SavePdfToStorage(
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>?>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string>()))
                .ReturnsAsync(true);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_ReturnsSuccess_WithGeneratedFileId()
        {
            SetupSuccessfulRender();
            SetupSuccessfulStorage();
            var sut = CreateSut();

            var result = await sut.CreateSinglePdfFromHtmlAsync(ValidRequest());

            Assert.True(result.IsSuccess);
            Assert.Equal("PDF created", result.Message);
            Assert.False(string.IsNullOrWhiteSpace(result.FileId));
            _storageMock.Verify(s => s.SavePdfToStorage(
                It.IsAny<Stream>(),
                result.FileId!,
                "invoice-123.pdf",
                It.IsAny<Dictionary<string, string>?>(),
                PdfGeneratorConstants.SinglePdfGeneratedFilesDirectory,
                "proj_1",
                "Private"), Times.Once);
            _engineMock.Verify(e => e.ConvertHtmlToPdfAsync(It.IsAny<string>(), It.IsAny<PdfGenerationOptions>()), Times.Once);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_UsesCallerSuppliedOutputFileId_AndPublicAccessModifier()
        {
            SetupSuccessfulRender();
            SetupSuccessfulStorage();
            var sut = CreateSut();
            var request = ValidRequest();
            request.OutputFileId = "file_cert_001";
            request.OutputFileName = "cert.pdf";
            request.AccessModifier = "Public";

            var result = await sut.CreateSinglePdfFromHtmlAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("file_cert_001", result.FileId);
            _storageMock.Verify(s => s.SavePdfToStorage(
                It.IsAny<Stream>(),
                "file_cert_001",
                "cert.pdf",
                It.IsAny<Dictionary<string, string>?>(),
                PdfGeneratorConstants.SinglePdfGeneratedFilesDirectory,
                "proj_1",
                "Public"), Times.Once);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_EchoesMessageCoRelationId()
        {
            SetupSuccessfulRender();
            SetupSuccessfulStorage();
            var sut = CreateSut();
            var request = ValidRequest();
            request.MessageCoRelationId = "corr-42";

            var result = await sut.CreateSinglePdfFromHtmlAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("corr-42", result.MessageCoRelationId);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_PassesHeaderFooterAndPageNumberOptions()
        {
            PdfGenerationOptions? captured = null;
            _engineMock
                .Setup(e => e.ConvertHtmlToPdfAsync(It.IsAny<string>(), It.IsAny<PdfGenerationOptions>()))
                .Callback<string, PdfGenerationOptions>((_, options) => captured = options)
                .ReturnsAsync(() => new MemoryStream([0x25, 0x50, 0x44, 0x46]));
            SetupSuccessfulStorage();
            var sut = CreateSut();
            var request = ValidRequest();
            request.HeaderHtml = "<div>header</div>";
            request.FooterHtml = "<div>footer</div>";
            request.HeaderHeight = "20";
            request.FooterHeight = "15";
            request.IsPageNumberEnabled = true;
            request.IsTotalPageCountEnabled = true;
            request.PageNumberText = "Page";

            var result = await sut.CreateSinglePdfFromHtmlAsync(request);

            Assert.True(result.IsSuccess);
            Assert.NotNull(captured);
            Assert.Equal("<div>header</div>", captured!.HeaderHtml);
            Assert.Equal("<div>footer</div>", captured.FooterHtml);
            Assert.Equal(20, captured.HeaderHeight);
            Assert.Equal(15, captured.FooterHeight);
            Assert.True(captured.IsPageNumberEnabled);
            Assert.True(captured.IsTotalPageCountEnabled);
            Assert.Equal("Page", captured.PageNumberText);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_RejectsMissingProjectKey_WithoutRendering()
        {
            var sut = CreateSut();
            var request = ValidRequest();
            request.ProjectKey = " ";

            var result = await sut.CreateSinglePdfFromHtmlAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal("ProjectKey is required", result.Message);
            Assert.Null(result.FileId);
            VerifyNoRenderOrStorage();
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_RejectsMissingHtmlContent_WithoutRendering()
        {
            var sut = CreateSut();
            var request = ValidRequest();
            request.HtmlContent = "";

            var result = await sut.CreateSinglePdfFromHtmlAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal("HtmlContent is required", result.Message);
            VerifyNoRenderOrStorage();
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_RejectsMissingOutputFileName_WithoutRendering()
        {
            var sut = CreateSut();
            var request = ValidRequest();
            request.OutputFileName = null!;

            var result = await sut.CreateSinglePdfFromHtmlAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal("OutputFileName is required", result.Message);
            VerifyNoRenderOrStorage();
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_RejectsOversizedHtmlContent_WithoutRendering()
        {
            var sut = CreateSut();
            var request = ValidRequest();
            request.HtmlContent = new string('a', SinglePdfGeneratorService.MaxHtmlContentBytes + 1);

            var result = await sut.CreateSinglePdfFromHtmlAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(
                $"HtmlContent exceeds maximum size of {SinglePdfGeneratorService.MaxHtmlContentBytes} bytes",
                result.Message);
            VerifyNoRenderOrStorage();
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_ReturnsRenderFailed_WhenEngineReturnsNull()
        {
            _engineMock
                .Setup(e => e.ConvertHtmlToPdfAsync(It.IsAny<string>(), It.IsAny<PdfGenerationOptions>()))
                .ReturnsAsync((Stream?)null);
            var sut = CreateSut();

            var result = await sut.CreateSinglePdfFromHtmlAsync(ValidRequest());

            Assert.False(result.IsSuccess);
            Assert.Equal("PDF render failed", result.Message);
            Assert.Null(result.FileId);
            _storageMock.Verify(s => s.SavePdfToStorage(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_ReturnsRenderFailed_WhenEngineReturnsEmptyStream()
        {
            _engineMock
                .Setup(e => e.ConvertHtmlToPdfAsync(It.IsAny<string>(), It.IsAny<PdfGenerationOptions>()))
                .ReturnsAsync(() => new MemoryStream());
            var sut = CreateSut();

            var result = await sut.CreateSinglePdfFromHtmlAsync(ValidRequest());

            Assert.False(result.IsSuccess);
            Assert.Equal("PDF render failed", result.Message);
            _storageMock.Verify(s => s.SavePdfToStorage(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_RejectsOversizedRenderedPdf_WithoutStoring()
        {
            var oversized = new byte[SinglePdfGeneratorService.MaxRenderedPdfBytes + 1];
            _engineMock
                .Setup(e => e.ConvertHtmlToPdfAsync(It.IsAny<string>(), It.IsAny<PdfGenerationOptions>()))
                .ReturnsAsync(() => new MemoryStream(oversized));
            var sut = CreateSut();

            var result = await sut.CreateSinglePdfFromHtmlAsync(ValidRequest());

            Assert.False(result.IsSuccess);
            Assert.Equal(
                $"Rendered PDF exceeds maximum size of {SinglePdfGeneratorService.MaxRenderedPdfBytes} bytes",
                result.Message);
            _storageMock.Verify(s => s.SavePdfToStorage(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_ReturnsStorageUploadFailed_WhenSaveReturnsFalse()
        {
            SetupSuccessfulRender();
            _storageMock
                .Setup(s => s.SavePdfToStorage(
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>?>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string>()))
                .ReturnsAsync(false);
            var sut = CreateSut();

            var result = await sut.CreateSinglePdfFromHtmlAsync(ValidRequest());

            Assert.False(result.IsSuccess);
            Assert.Equal("Storage upload failed", result.Message);
            Assert.Null(result.FileId);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_ReturnsTimeout_WhenRenderExceedsBound()
        {
            _engineMock
                .Setup(e => e.ConvertHtmlToPdfAsync(It.IsAny<string>(), It.IsAny<PdfGenerationOptions>()))
                .Returns(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    return new MemoryStream([0x25, 0x50, 0x44, 0x46]);
                });
            var sut = CreateSut(TimeSpan.FromMilliseconds(50));

            var result = await sut.CreateSinglePdfFromHtmlAsync(ValidRequest());

            Assert.False(result.IsSuccess);
            Assert.Equal("PDF render timed out after 30 seconds", result.Message);
            _storageMock.Verify(s => s.SavePdfToStorage(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CreateSinglePdfFromHtmlAsync_DefaultsAccessModifierToPrivate_WhenOmitted()
        {
            SetupSuccessfulRender();
            SetupSuccessfulStorage();
            var sut = CreateSut();
            var request = ValidRequest();
            request.AccessModifier = null;

            var result = await sut.CreateSinglePdfFromHtmlAsync(request);

            Assert.True(result.IsSuccess);
            _storageMock.Verify(s => s.SavePdfToStorage(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                "Private"), Times.Once);
        }

        private void VerifyNoRenderOrStorage()
        {
            _engineMock.Verify(
                e => e.ConvertHtmlToPdfAsync(It.IsAny<string>(), It.IsAny<PdfGenerationOptions>()),
                Times.Never);
            _storageMock.Verify(s => s.SavePdfToStorage(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>()), Times.Never);
        }
    }
}
