using DomainService.Storage;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StorageDriver;

namespace XUnitTest.Mail
{
    public class StorageMailAttachmentProviderTests
    {
        [Fact]
        public async Task GetAttachmentsAsync_ReturnsEmptyList_WhenNoAttachmentsExist()
        {
            var storageDriver = new Mock<IStorageDriverService>();
            var provider = new StorageMailAttachmentProvider(
                storageDriver.Object,
                Mock.Of<IHttpClientFactory>(),
                new ConfigurationBuilder().Build(),
                NullLogger<StorageMailAttachmentProvider>.Instance);

            var result = await provider.GetAttachmentsAsync(new MailToBeSent
            {
                Attachments = []
            });

            Assert.Empty(result);
            storageDriver.Verify(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()), Times.Never);
        }

        [Fact]
        public async Task GetAttachmentsAsync_ThrowsAttachmentException_WhenStorageUrlIsMissing()
        {
            var storageDriver = new Mock<IStorageDriverService>();
            storageDriver
                .Setup(x => x.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
                .ReturnsAsync((FileResponse?)null);

            var provider = new StorageMailAttachmentProvider(
                storageDriver.Object,
                Mock.Of<IHttpClientFactory>(),
                new ConfigurationBuilder().Build(),
                NullLogger<StorageMailAttachmentProvider>.Instance);

            await Assert.ThrowsAsync<MailAttachmentException>(() => provider.GetAttachmentsAsync(new MailToBeSent
            {
                Attachments = ["file-1"]
            }));
        }
    }
}
