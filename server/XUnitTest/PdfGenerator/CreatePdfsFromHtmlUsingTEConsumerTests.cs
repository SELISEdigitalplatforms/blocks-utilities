using System.Net;
using DomainService.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using StorageDriver;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.TemplateEngine.service;
using Worker.Consumers.PdfGenerator;

namespace XUnitTest.PdfGenerator
{
    public class CreatePdfsFromHtmlUsingTEConsumerTests
    {
        private sealed class TestableConsumer : CreatePdfsFromHtmlUsingTEConsumer
        {
            public TestableConsumer(PdfStorageHelper storageHelper)
                : base(
                    Mock.Of<ILogger<CreatePdfsFromHtmlUsingTEConsumer>>(),
                    storageHelper,
                    Mock.Of<IPdfEngineProvider>(),
                    Mock.Of<IPdfGeneratorRepository>(),
                    Mock.Of<IPdfGeneratorNotificationService>(),
                    new TemplateRenderingService(Mock.Of<ILogger<TemplateRenderingService>>()))
            {
            }

            public Task<string?> Generate(CreateFromHtmlUsingTECommand command) =>
                GenerateHtmlFromTemplate(command, "project-1", "project-1");
        }

        // The render used to be queued and its output read back at once, so the HTML was never there.
        // Only the template may be fetched; nothing reads an intermediate rendered_* file.
        [Fact]
        public async Task GenerateHtmlFromTemplate_RendersTheTemplateInProcess()
        {
            var storage = new Mock<IStorageDriverService>();
            storage
                .Setup(x => x.GetUrlForDownloadFileAsync(It.Is<GetFileRequest>(r => r.FileId == "tpl-1")))
                .ReturnsAsync(new FileResponse { Url = "https://storage.example/tpl-1" });

            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<p>{{ Name }}: {% for s in Signatures %}{{ s.Email }} {% endfor %}</p>")
            });
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

            var consumer = new TestableConsumer(new PdfStorageHelper(
                Mock.Of<ILogger<PdfStorageHelper>>(), storage.Object, factory.Object));

            var html = await consumer.Generate(new CreateFromHtmlUsingTECommand
            {
                TemplateFileId = "tpl-1",
                MetaDataList =
                [
                    new PdfMetaData { Key = "Name", Value = "Contract" },
                    new PdfMetaData { Key = "Signatures", Value = new[] { new Dictionary<string, object> { ["Email"] = "ada@example.com" } } }
                ]
            });

            Assert.Equal("<p>Contract: ada@example.com </p>", html);
            storage.Verify(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()), Times.Once);
        }

        [Fact]
        public async Task GenerateHtmlFromTemplate_ReturnsNull_WhenTemplateIsMissing()
        {
            var storage = new Mock<IStorageDriverService>();
            storage
                .Setup(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
                .ReturnsAsync((FileResponse?)null);

            var consumer = new TestableConsumer(new PdfStorageHelper(
                Mock.Of<ILogger<PdfStorageHelper>>(), storage.Object, Mock.Of<IHttpClientFactory>()));

            Assert.Null(await consumer.Generate(new CreateFromHtmlUsingTECommand { TemplateFileId = "missing" }));
        }
    }
}
