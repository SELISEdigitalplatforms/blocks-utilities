using Blocks.Genesis;
using DomainService.Storage;
using Microsoft.Extensions.Logging;
using Utility.DomainService.Shared.Utilities;
using Newtonsoft.Json;
using Storage.DomainService.Enums;
using StorageDriver;
using Utility.DomainService.Storage;

namespace Utility.DomainService.TemplateEngine.service
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class StorageHelper : StorageHelperBase
    {
        private readonly IStorageDriverService _storageDriverService;

        public StorageHelper(
            ILogger<StorageHelper> logger,
            IStorageDriverService storageDriverService,
            IHttpClientFactory httpClientFactory,
            StorageDirectoryResolver? directories = null)
            : base(logger, httpClientFactory, directories)
        {
            _storageDriverService = storageDriverService;
        }

        /// <summary>
        /// Saves a file to storage
        /// </summary>
        public async Task<bool> SaveFileToStorage(MemoryStream inputStream, string fileId, string fileName, Dictionary<string, string>? metadata = null, string parentDirectoryId = "Blocks-Template-Files")
        {
            _logger.LogInformation("SaveFileToStorage: Saving file to storage -- fileId={FileId}, fileName={FileName}", LogSanitizer.Scrub(fileId), LogSanitizer.Scrub(fileName));

            Stream stream = new MemoryStream();
            await stream.WriteAsync(inputStream.ToArray(), 0, inputStream.ToArray().Length);
            stream.Seek(0, SeekOrigin.Begin);

            // Format metadata for storage service (MetaValue structure with Type and Value)
            var formattedMetadata = new Dictionary<string, object>();
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    formattedMetadata[kvp.Key] = new { Type = "String", Value = kvp.Value };
                }
            }

            var parentDirectory = await ResolveParentDirectoryAsync(parentDirectoryId);
            if (parentDirectory is null)
            {
                _logger.LogError("SaveFileToStorage: No storage directory for {Directory}, fileId={FileId}", LogSanitizer.Scrub(parentDirectoryId), LogSanitizer.Scrub(fileId));
                return false;
            }

            var payload = new GetPreSignedUrlForUploadRequest
            {
                ItemId = fileId,
                MetaData = formattedMetadata.Count > 0 ? JsonConvert.SerializeObject(formattedMetadata) : string.Empty,
                Name = fileName,
                ParentDirectoryId = parentDirectory,
                Tags = "[\"File\"]",
                AccessModifier = "Private",
            };

            _logger.LogInformation(
                "SaveFileToStorage: Requesting upload URL fileId={FileId}, name={Name}, parentDirectory={ParentDirectory}, " +
                "accessModifier={AccessModifier}, tags={Tags}, metadataKeys={MetadataKeys}",
                LogSanitizer.Scrub(fileId), LogSanitizer.Scrub(payload.Name), LogSanitizer.Scrub(payload.ParentDirectoryId), LogSanitizer.Scrub(payload.AccessModifier), LogSanitizer.Scrub(payload.Tags),
                formattedMetadata.Count);

            var fileInfo = await _storageDriverService.GetPerSignedUrlForUploadAsync(payload);
            if (fileInfo == null || string.IsNullOrEmpty(fileInfo.UploadUrl))
            {
                _logger.LogError(
                    "SaveFileToStorage: Failed to get pre-signed URL for fileId={FileId}, response: {Response}",
                    LogSanitizer.Scrub(fileId), Describe(fileInfo));
                return false;
            }

            // The URL itself is a signed credential and is never logged; what it allows is.
            _logger.LogInformation(
                "SaveFileToStorage: Got upload URL fileId={FileId}, fileVersionId={FileVersionId}, " +
                "expiresAtUtc={ExpiresAtUtc}, completionRequired={CompletionRequired}, requiredHeaders={RequiredHeaders}",
                LogSanitizer.Scrub(fileId), LogSanitizer.Scrub(fileInfo.FileVersionId), fileInfo.UploadUrlExpiresAtUtc, fileInfo.UploadCompletionRequired,
                LogSanitizer.Scrub(fileInfo.RequiredHeaders is null ? "none" : string.Join(",", fileInfo.RequiredHeaders.Keys)));

            var httpClient = CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Put, fileInfo.UploadUrl)
            {
                Content = new StreamContent(stream)
            };

            request.Headers.Add("x-ms-blob-type", "BlockBlob");

            var httpResponseMessage = await httpClient.SendAsync(request);
            stream.Close();

            if (!httpResponseMessage.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "SaveFileToStorage: Failed to upload file fileId={FileId}, StatusCode={StatusCode}, body: {Body}",
                    LogSanitizer.Scrub(fileId), httpResponseMessage.StatusCode, await DescribeFailureAsync(httpResponseMessage));
                return false;
            }

            _logger.LogInformation("SaveFileToStorage: Successfully saved file fileId={FileId}", LogSanitizer.Scrub(fileId));

            if (!fileInfo.UploadCompletionRequired)
            {
                return true;
            }

            var completion = await _storageDriverService.CompleteUploadAsync(new CompleteUploadRequest
            {
                FileId = fileId,
                FileVersionId = fileInfo.FileVersionId,
            });

            if (completion?.VerificationStatus != FileVerificationStatus.Verified)
            {
                _logger.LogError(
                    "SaveFileToStorage: Upload completion rejected fileId={FileId}, status={VerificationStatus}, " +
                    "reason={RejectionReason}, response: {Response}",
                    LogSanitizer.Scrub(fileId), completion?.VerificationStatus, LogSanitizer.Scrub(completion?.RejectionReason), Describe(completion));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets file content as string
        /// </summary>
        public async Task<string?> GetFileContentAsString(string fileId, string projectKey)
        {
            _logger.LogInformation("GetFileContentAsString: Getting file content for fileId={FileId}", LogSanitizer.Scrub(fileId));

            // Get file metadata and URL
            var fileData = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest
            {
                FileId = fileId
            });

            if (fileData == null || string.IsNullOrEmpty(fileData.Url))
            {
                _logger.LogError(
                    "GetFileContentAsString: File data is null or URL is empty for fileId={FileId}, response: {Response}",
                    LogSanitizer.Scrub(fileId), Describe(fileData));
                return null;
            }

            _logger.LogInformation("GetFileContentAsString: Got file URL for fileId={FileId}", LogSanitizer.Scrub(fileId));

            // Download file content
            var stream = await GetFileStreamFromUrl(fileData.Url);
            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();

            _logger.LogInformation("GetFileContentAsString: Successfully read file content, length={ContentLength}", content.Length);
            return content;
        }
    }
}


