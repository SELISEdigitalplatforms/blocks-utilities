using System.Net;
using DomainService.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using StorageDriver;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.Storage;

namespace XUnitTest.PdfGenerator
{
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
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

        /// <summary>
        /// Builds a factory that hands out clients over <paramref name="handler"/>, the way
        /// <c>AddHttpClient</c> does in a host.
        /// </summary>
        private static IHttpClientFactory FactoryFor(HttpMessageHandler handler)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory
                .Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(handler, disposeHandler: false));
            return factory.Object;
        }

        private static IHttpClientFactory NeverCalledFactory()
        {
            var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            return factory.Object;
        }

        [Fact]
        public async Task SavePdfToStorage_ReturnsFalse_WhenUploadUrlMissing()
        {
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync((GetPreSignedUrlForUploadResponse?)null);

            var helper = new PdfStorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, NeverCalledFactory());
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

            var helper = new PdfStorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, NeverCalledFactory());

            var result = await helper.GetPdfStream("file1");

            Assert.Null(result);
        }

        [Fact]
        public async Task An_upload_goes_through_the_factory_rather_than_its_own_client()
        {
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync(new GetPreSignedUrlForUploadResponse
                {
                    UploadUrl = "https://storage.example/upload"
                });

            var handler = new FakeHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK));
            var helper = new PdfStorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, FactoryFor(handler));

            var result = await helper.SavePdfToStorage(
                new MemoryStream([1, 2, 3]), "file1", "test.pdf");

            Assert.True(result);
            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("BlockBlob", Assert.Single(request.Headers.GetValues("x-ms-blob-type")));
        }

        [Fact]
        public async Task A_failed_upload_reads_as_false()
        {
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync(new GetPreSignedUrlForUploadResponse
                {
                    UploadUrl = "https://storage.example/upload"
                });

            var handler = new FakeHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
            var helper = new PdfStorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, FactoryFor(handler));

            var result = await helper.SavePdfToStorage(
                new MemoryStream([1]), "file1", "test.pdf");

            Assert.False(result);
        }

        [Fact]
        public void The_storage_client_name_is_what_the_modules_register()
        {
            // The registration and the lookup are in different projects; if this constant drifts,
            // CreateClient silently returns a default-configured client instead of failing.
            Assert.Equal("utility-storage", StorageHelperBase.StorageHttpClientName);
        }
    }
}
