using System.Net;
using DomainService.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using StorageDriver;
using Utility.DomainService.Storage;
using Utility.DomainService.TemplateEngine.service;

namespace XUnitTest.TemplateEngine
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

    /// <summary>
    /// Covers <see cref="StorageHelper.SaveFileToStorage"/>'s Phase 1 upload-completion wiring:
    /// the provider PUT alone is not success when completion is required, and a rejection must
    /// surface as a failed save rather than silently reporting true.
    /// </summary>
    public class StorageHelperTests
    {
        private readonly Mock<ILogger<StorageHelper>> _loggerMock = new();
        private readonly Mock<IStorageDriverService> _storageDriverMock = new();

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
        public async Task SaveFileToStorage_ReturnsFalse_WhenUploadUrlMissing()
        {
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync((GetPreSignedUrlForUploadResponse?)null);

            var helper = new StorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, NeverCalledFactory());
            var result = await helper.SaveFileToStorage(new MemoryStream([1]), "file1", "test.docx");

            Assert.False(result);
        }

        [Fact]
        public async Task SaveFileToStorage_UsesPrivateAccessModifier()
        {
            GetPreSignedUrlForUploadRequest? captured = null;
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .Callback<GetPreSignedUrlForUploadRequest>(r => captured = r)
                .ReturnsAsync(new GetPreSignedUrlForUploadResponse
                {
                    UploadUrl = "https://storage.example/upload",
                });

            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var helper = new StorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, FactoryFor(handler));

            await helper.SaveFileToStorage(new MemoryStream([1]), "file1", "test.docx");

            Assert.Equal("Private", captured!.AccessModifier);
        }

        [Fact]
        public async Task SaveFileToStorage_ReturnsFalse_WhenTheProviderUploadFails()
        {
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync(new GetPreSignedUrlForUploadResponse
                {
                    UploadUrl = "https://storage.example/upload",
                });

            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
            var helper = new StorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, FactoryFor(handler));

            var result = await helper.SaveFileToStorage(new MemoryStream([1]), "file1", "test.docx");

            Assert.False(result);
        }

        [Fact]
        public async Task SaveFileToStorage_SkipsCompletion_WhenNotRequired()
        {
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync(new GetPreSignedUrlForUploadResponse
                {
                    UploadUrl = "https://storage.example/upload",
                    UploadCompletionRequired = false,
                });

            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var helper = new StorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, FactoryFor(handler));

            var result = await helper.SaveFileToStorage(new MemoryStream([1]), "file1", "test.docx");

            Assert.True(result);
            _storageDriverMock.Verify(
                x => x.CompleteUploadAsync(It.IsAny<CompleteUploadRequest>()), Times.Never);
        }

        [Fact]
        public async Task SaveFileToStorage_CallsCompletion_AndSucceeds_WhenVerified()
        {
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync(new GetPreSignedUrlForUploadResponse
                {
                    UploadUrl = "https://storage.example/upload",
                    FileVersionId = "v1",
                    UploadCompletionRequired = true,
                });
            _storageDriverMock
                .Setup(x => x.CompleteUploadAsync(It.Is<CompleteUploadRequest>(
                    r => r.FileId == "file1" && r.FileVersionId == "v1")))
                .ReturnsAsync(new CompleteUploadResponse
                {
                    IsSuccess = true,
                    VerificationStatus = Storage.DomainService.Enums.FileVerificationStatus.Verified,
                });

            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var helper = new StorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, FactoryFor(handler));

            var result = await helper.SaveFileToStorage(new MemoryStream([1]), "file1", "test.docx");

            Assert.True(result);
        }

        [Fact]
        public async Task SaveFileToStorage_ReturnsFalse_WhenCompletionRejects()
        {
            _storageDriverMock
                .Setup(x => x.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ReturnsAsync(new GetPreSignedUrlForUploadResponse
                {
                    UploadUrl = "https://storage.example/upload",
                    FileVersionId = "v1",
                    UploadCompletionRequired = true,
                });
            _storageDriverMock
                .Setup(x => x.CompleteUploadAsync(It.IsAny<CompleteUploadRequest>()))
                .ReturnsAsync(new CompleteUploadResponse
                {
                    IsSuccess = true,
                    VerificationStatus = Storage.DomainService.Enums.FileVerificationStatus.Rejected,
                    RejectionReason = "real_file_type_mismatch",
                });

            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var helper = new StorageHelper(
                _loggerMock.Object, _storageDriverMock.Object, FactoryFor(handler));

            var result = await helper.SaveFileToStorage(new MemoryStream([1]), "file1", "test.docx");

            Assert.False(result);
        }
    }
}
