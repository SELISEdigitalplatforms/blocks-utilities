using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace XUnitTest.Mail
{
    public class MailCategoryResolverTests
    {
        [Fact]
        public async Task ResolveAsync_ReturnsNoAttachment_WhenMailHasNoAttachments()
        {
            var resolver = CreateResolver();

            var result = await resolver.ResolveAsync(CreateMail("invoice"));

            Assert.Equal(MailCategory.NoAttachment, result);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsSmallAttachment_WhenAllAttachmentsAreUnderOrAtThreshold()
        {
            var resolver = CreateResolver(
                metadata: new Dictionary<string, long?> { ["small-1"] = 1024, ["small-2"] = 3L * 1024 * 1024 });

            var result = await resolver.ResolveAsync(CreateMail("invoice", ["small-1", "small-2"]));

            Assert.Equal(MailCategory.SmallAttachment, result);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsLargeAttachment_WhenAnyAttachmentIsOverThreshold()
        {
            var resolver = CreateResolver(
                metadata: new Dictionary<string, long?> { ["large"] = 3L * 1024 * 1024 + 1 });

            var result = await resolver.ResolveAsync(CreateMail("invoice", ["large"]));

            Assert.Equal(MailCategory.LargeAttachment, result);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsLargeAttachment_WhenAttachmentSizeIsUnknown()
        {
            var resolver = CreateResolver(
                metadata: new Dictionary<string, long?> { ["unknown"] = null });

            var result = await resolver.ResolveAsync(CreateMail("invoice", ["unknown"]));

            Assert.Equal(MailCategory.LargeAttachment, result);
        }

        [Fact]
        public async Task ResolveAsync_IgnoresDuplicateAndEmptyAttachmentIds()
        {
            var resolver = CreateResolver(
                metadata: new Dictionary<string, long?> { ["small"] = 1024 });

            var result = await resolver.ResolveAsync(CreateMail("invoice", ["", "small", "small", " "]));

            Assert.Equal(MailCategory.SmallAttachment, result);
        }

        private static MailCategoryResolver CreateResolver(
            Dictionary<string, string?>? configurationValues = null,
            Dictionary<string, long?>? metadata = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configurationValues ?? new Dictionary<string, string?>())
                .Build();

            return new MailCategoryResolver(
                new FakeAttachmentMetadataProvider(metadata ?? new Dictionary<string, long?>()),
                configuration,
                NullLogger<MailCategoryResolver>.Instance);
        }

        private static MailToBeSent CreateMail(string purpose, IEnumerable<string>? attachments = null)
        {
            return new MailToBeSent
            {
                Name = purpose,
                Attachments = attachments ?? []
            };
        }

        private sealed class FakeAttachmentMetadataProvider : IMailAttachmentMetadataProvider
        {
            private readonly Dictionary<string, long?> _metadata;

            public FakeAttachmentMetadataProvider(Dictionary<string, long?> metadata)
            {
                _metadata = metadata;
            }

            public Task<MailAttachmentMetadata> GetMetadataAsync(string fileId, CancellationToken cancellationToken = default)
            {
                _metadata.TryGetValue(fileId, out var sizeInBytes);
                return Task.FromResult(new MailAttachmentMetadata(fileId, sizeInBytes));
            }
        }
    }
}
