using Blocks.Genesis;
using DomainService.Storage;
using Microsoft.Extensions.Logging;
using StorageDriver;

namespace Mail.DomainService.Mails
{
    public class StorageMailAttachmentMetadataProvider : IMailAttachmentMetadataProvider
    {
        private readonly IStorageDriverService _storageDriverService;
        private readonly ILogger<StorageMailAttachmentMetadataProvider> _logger;

        public StorageMailAttachmentMetadataProvider(
            IStorageDriverService storageDriverService,
            ILogger<StorageMailAttachmentMetadataProvider> logger)
        {
            _storageDriverService = storageDriverService;
            _logger = logger;
        }

        public async Task<MailAttachmentMetadata> GetMetadataAsync(string fileId, CancellationToken cancellationToken = default)
        {
            try
            {
                var fileData = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest
                {
                    FileId = fileId,
                    ProjectKey = BlocksContext.GetContext()?.TenantId ?? string.Empty
                });

                if (fileData == null)
                {
                    _logger.LogWarning("Attachment metadata lookup returned no data. FileId={FileId}", fileId);
                    return new MailAttachmentMetadata(fileId, null);
                }

                return new MailAttachmentMetadata(fileId, GetSizeInBytes(fileData));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attachment metadata lookup failed. FileId={FileId}", fileId);
                return new MailAttachmentMetadata(fileId, null);
            }
        }

        private static long? GetSizeInBytes(object source)
        {
            var sourceType = source.GetType();
            var value = sourceType.GetProperty("SizeInBytes")?.GetValue(source)
                ?? sourceType.GetProperty("Size")?.GetValue(source)
                ?? sourceType.GetProperty("Length")?.GetValue(source);

            return value switch
            {
                long longValue => longValue,
                int intValue => intValue,
                double doubleValue => Convert.ToInt64(doubleValue),
                decimal decimalValue => Convert.ToInt64(decimalValue),
                _ => null
            };
        }
    }
}
