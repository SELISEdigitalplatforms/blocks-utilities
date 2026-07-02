using Blocks.Genesis;
using DomainService.Storage;
using Mail.DomainService.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using StorageDriver;

namespace Mail.DomainService.Mails.Services.Attachments
{
    public class StorageMailAttachmentProvider : IMailAttachmentProvider
    {
        private const long InMemoryAttachmentMaxSizeInBytes = 3L * 1024 * 1024;
        private const string TempDirectoryConfigurationKey = "MailAttachments:TempDirectory";
        private const string TempDirectoryEnvironmentVariable = "MAIL_ATTACHMENTS_TEMP_DIRECTORY";

        private readonly IStorageDriverService _storageDriverService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StorageMailAttachmentProvider> _logger;

        public StorageMailAttachmentProvider(
            IStorageDriverService storageDriverService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<StorageMailAttachmentProvider> logger)
        {
            _storageDriverService = storageDriverService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IReadOnlyList<MailAttachment>> GetAttachmentsAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default)
        {
            var attachmentIds = mailToBeSent.Attachments?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            if (attachmentIds.Length == 0)
            {
                return Array.Empty<MailAttachment>();
            }

            var attachments = new List<MailAttachment>(attachmentIds.Length);

            foreach (var attachmentId in attachmentIds)
            {
                var attachment = await GetAttachmentAsync(attachmentId, cancellationToken);
                attachments.Add(attachment);
            }

            return attachments;
        }

        private async Task<MailAttachment> GetAttachmentAsync(string attachmentId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Resolving mail attachment from storage. FileId={FileId}", attachmentId);

            var fileData = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest
            {
                FileId = attachmentId,
                ProjectKey = BlocksContext.GetContext()?.TenantId ?? string.Empty
            });

            if (fileData == null || string.IsNullOrWhiteSpace(fileData.Url))
            {
                throw new MailAttachmentException($"Download URL was not found for attachment file id '{attachmentId}'.");
            }

            var fileName = GetStringProperty(fileData, "Name") ?? attachmentId;
            var contentType = MimeTypes.GetMimeType(fileName);

            var httpClient = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, fileData.Url);
            request.Headers.Add("X-Blocks-Key", BlocksContext.GetContext()?.TenantId);

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new MailAttachmentException($"Attachment download failed for file id '{attachmentId}' with status code '{response.StatusCode}'.");
            }

            var contentLength = response.Content.Headers.ContentLength;

            if (contentLength is > 0 and <= InMemoryAttachmentMaxSizeInBytes)
            {
                await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var memoryStream = new MemoryStream((int)contentLength.Value);
                await downloadStream.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;

                _logger.LogInformation(
                    "Resolved mail attachment in memory. FileId={FileId}, FileName={FileName}, SizeInBytes={SizeInBytes}",
                    attachmentId,
                    fileName,
                    memoryStream.Length);

                return new MailAttachment(attachmentId, fileName, contentType, memoryStream, memoryStream.Length);
            }

            return await DownloadToTemporaryFileAsync(
                attachmentId,
                fileName,
                contentType,
                response,
                cancellationToken);
        }

        private async Task<MailAttachment> DownloadToTemporaryFileAsync(
            string attachmentId,
            string fileName,
            string contentType,
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            var tempDirectory = GetTempDirectory();
            Directory.CreateDirectory(tempDirectory);

            var tempFilePath = Path.Combine(tempDirectory, $"mail-attachment-{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var tempWriteStream = new FileStream(
                    tempFilePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await downloadStream.CopyToAsync(tempWriteStream, cancellationToken);
                }

                var fileInfo = new FileInfo(tempFilePath);

                _logger.LogInformation(
                    "Resolved mail attachment in temporary storage. FileId={FileId}, FileName={FileName}, SizeInBytes={SizeInBytes}, TempDirectory={TempDirectory}",
                    attachmentId,
                    fileName,
                    fileInfo.Length,
                    tempDirectory);

                var readStream = new FileStream(
                    tempFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                return new MailAttachment(attachmentId, fileName, contentType, readStream, fileInfo.Length, tempFilePath);
            }
            catch
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }

                throw;
            }
        }

        private string GetTempDirectory()
        {
            return _configuration[TempDirectoryConfigurationKey]
                ?? Environment.GetEnvironmentVariable(TempDirectoryEnvironmentVariable)
                ?? Path.GetTempPath();
        }

        private static string? GetStringProperty(object source, string propertyName)
        {
            return source.GetType().GetProperty(propertyName)?.GetValue(source) as string;
        }
    }

    public class MailAttachmentException : Exception
    {
        public MailAttachmentException(string message) : base(message)
        {
        }

        public MailAttachmentException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
