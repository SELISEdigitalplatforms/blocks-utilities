using DomainService.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using StorageDriver;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    internal class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }


    public class PdfStorageHelperTests
    {
        private readonly Mock<ILogger<PdfStorageHelper>> _loggerMock;
        private readonly Mock<IStorageDriverService> _storageDriverMock;

        public PdfStorageHelperTests()
        {
            _loggerMock = new Mock<ILogger<PdfStorageHelper>>();
            _storageDriverMock = new Mock<IStorageDriverService>();
        }

        [Fact]
        public async Task SavePdfToStorage_ReturnsFalse_WhenUploadUrlMissing()
        {
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync((GetPreSignedUrlForUploadResponse?)null);

            var helper = new PdfStorageHelper(_loggerMock.Object, _storageDriverMock.Object);
            var input = new MemoryStream(new byte[] { 1 });
            var result = await helper.SavePdfToStorage(input, "file1", "test.pdf");

            Assert.False(result);
        }

        [Fact]
        public async Task GetPdfStream_ReturnsNull_WhenUrlMissing()
        {
            _storageDriverMock
                .Setup(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
                .ReturnsAsync((FileResponse?)null);

            var helper = new PdfStorageHelper(_loggerMock.Object, _storageDriverMock.Object);

            var result = await helper.GetPdfStream("file1");

            Assert.Null(result);
        }
    }
}
