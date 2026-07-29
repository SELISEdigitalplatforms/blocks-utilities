using Blocks.Genesis;
using DomainService.Storage;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
            IStorageDriverService storageDriverService)
            : base(logger)
        {
            _storageDriverService = storageDriverService;
        }

        /// <summary>
        /// Saves a file to storage
        /// </summary>
        public async Task<bool> SaveFileToStorage(MemoryStream inputStream, string fileId, string fileName, Dictionary<string, string>? metadata = null, string parentDirectoryId = "Blocks-Template-Files")
        {
            _logger.LogInformation("SaveFileToStorage: Saving file to storage -- fileId={FileId}, fileName={FileName}", fileId, fileName);

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

            var payload = new GetPreSignedUrlForUploadRequest
            {
                ItemId = fileId,
                MetaData = formattedMetadata.Count > 0 ? JsonConvert.SerializeObject(formattedMetadata) : string.Empty,
                Name = fileName,
                ParentDirectoryId = parentDirectoryId,
                Tags = "[\"File\"]"
            };

            var fileInfo = await _storageDriverService.GetPerSignedUrlForUploadAsync(payload);
            if (fileInfo == null || string.IsNullOrEmpty(fileInfo.UploadUrl))
            {
                _logger.LogError("SaveFileToStorage: Failed to get pre-signed URL for fileId={FileId}", fileId);
                return false;
            }

            _logger.LogInformation("SaveFileToStorage: Got upload URL for fileId={FileId}", fileId);

            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Put, fileInfo.UploadUrl)
            {
                Content = new StreamContent(stream)
            };

            request.Headers.Add("x-ms-blob-type", "BlockBlob");

            var httpResponseMessage = await httpClient.SendAsync(request);
            stream.Close();

            if (httpResponseMessage.IsSuccessStatusCode)
            {
                _logger.LogInformation("SaveFileToStorage: Successfully saved file fileId={FileId}", fileId);
                return true;
            }

            _logger.LogError("SaveFileToStorage: Failed to upload file fileId={FileId}, StatusCode={StatusCode}", fileId, httpResponseMessage.StatusCode);
            return false;
        }

        /// <summary>
        /// Gets file content as string
        /// </summary>
        public async Task<string?> GetFileContentAsString(string fileId, string projectKey)
        {
            _logger.LogInformation("GetFileContentAsString: Getting file content for fileId={FileId}", fileId);

            // Get file metadata and URL
            var fileData = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest
            {
                FileId = fileId
            });

            if (fileData == null || string.IsNullOrEmpty(fileData.Url))
            {
                _logger.LogError("GetFileContentAsString: File data is null or URL is empty for fileId={FileId}", fileId);
                return null;
            }

            _logger.LogInformation("GetFileContentAsString: Got file URL for fileId={FileId}", fileId);

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


