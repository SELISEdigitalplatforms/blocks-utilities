using Blocks.Genesis;
using DomainService.Storage;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StorageDriver;
using Utility.DomainService.Storage;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Storage helper for PDF generator operations
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfStorageHelper : StorageHelperBase
    {
        private readonly IStorageDriverService _storageDriverService;

        public PdfStorageHelper(
            ILogger<PdfStorageHelper> logger,
            IStorageDriverService storageDriverService,
            IHttpClientFactory httpClientFactory)
            : base(logger, httpClientFactory)
        {
            _storageDriverService = storageDriverService;
        }

        /// <summary>
        /// Saves a PDF file to storage
        /// </summary>
        public async Task<bool> SavePdfToStorage(Stream inputStream, string fileId, string fileName, Dictionary<string, string>? metadata = null, string parentDirectoryId = "Blocks-PDF-Generated-Files", string? projectKey = null)
        {
            _logger.LogInformation("SavePdfToStorage: Saving PDF to storage -- fileId={FileId}, fileName={FileName}", fileId, fileName);

            var stream = new MemoryStream();
            await inputStream.CopyToAsync(stream);
            stream.Seek(0, SeekOrigin.Begin);

            // Format metadata for storage service
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
                Tags = "[\"PDF\"]"
            };

            var fileInfo = await _storageDriverService.GetPerSignedUrlForUploadAsync(payload);
            if (fileInfo == null || string.IsNullOrEmpty(fileInfo.UploadUrl))
            {
                _logger.LogError("SavePdfToStorage: Failed to get pre-signed URL for fileId={FileId}", fileId);
                return false;
            }

            _logger.LogInformation("SavePdfToStorage: Got upload URL for fileId={FileId}", fileId);

            var httpClient = CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Put, fileInfo.UploadUrl)
            {
                Content = new StreamContent(stream)
            };

            request.Headers.Add("x-ms-blob-type", "BlockBlob");
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");

            var httpResponseMessage = await httpClient.SendAsync(request);
            stream.Close();

            if (httpResponseMessage.IsSuccessStatusCode)
            {
                _logger.LogInformation("SavePdfToStorage: Successfully saved PDF fileId={FileId}", fileId);
                return true;
            }

                _logger.LogError("SavePdfToStorage: Failed to upload PDF fileId={FileId}, StatusCode={StatusCode}", fileId, httpResponseMessage.StatusCode);
            return false;
        }

        /// <summary>
        /// Gets PDF file as stream
        /// </summary>
        public async Task<Stream?> GetPdfStream(string fileId, string? projectKey = null)
        {
            _logger.LogInformation("GetPdfStream: Getting PDF stream for fileId={FileId}", fileId);

            var fileData = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest
            {
                FileId = fileId
            });

            if (fileData == null || string.IsNullOrEmpty(fileData.Url))
            {
                _logger.LogError("GetPdfStream: File data is null or URL is empty for fileId={FileId}", fileId);
                return null;
            }

            _logger.LogInformation("GetPdfStream: Got file URL for fileId={FileId}", fileId);

            return await GetFileStreamFromUrl(fileData.Url);
        }

        /// <summary>
        /// Gets HTML file content as string
        /// </summary>
        public async Task<string?> GetHtmlContentAsString(string fileId, string? projectKey = null)
        {
            _logger.LogInformation("GetHtmlContentAsString: Getting HTML content for fileId={FileId}", fileId);

            var fileData = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest
            {
                FileId = fileId
            });

            if (fileData == null || string.IsNullOrEmpty(fileData.Url))
            {
                _logger.LogError("GetHtmlContentAsString: File data is null or URL is empty for fileId={FileId}", fileId);
                return null;
            }

            _logger.LogInformation("GetHtmlContentAsString: Got file URL for fileId={FileId}", fileId);

            var stream = await GetFileStreamFromUrl(fileData.Url);
            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            
            _logger.LogInformation("GetHtmlContentAsString: Successfully read HTML content, length={ContentLength}", content.Length);
            return content;
        }

        /// <summary>
        /// Resolves a file's storage record: its name, the directory it lives in, and the URL its
        /// content can be downloaded from.
        /// </summary>
        /// <remarks>
        /// In-place document conversion needs the record, not just the bytes. The name carries the
        /// extension that decides whether the file can be converted at all, and the directory has to
        /// be preserved so replacing a file does not silently relocate it. Exposed separately from
        /// <see cref="GetPdfStream"/> so a caller can read the record, decide, and only then pay for
        /// the download.
        /// </remarks>
        public async Task<FileResponse?> GetFileRecord(string fileId, string? projectKey = null)
        {
            _logger.LogInformation("GetFileRecord: Getting storage record for fileId={FileId}", fileId);

            var fileData = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest
            {
                FileId = fileId
            });

            if (fileData == null || string.IsNullOrEmpty(fileData.Url))
            {
                _logger.LogError("GetFileRecord: File data is null or URL is empty for fileId={FileId}", fileId);
                return null;
            }

            return fileData;
        }

        /// <summary>
        /// Downloads the content of a record already resolved by <see cref="GetFileRecord"/>,
        /// without asking storage to resolve it a second time.
        /// </summary>
        public async Task<Stream?> GetStreamForRecord(FileResponse fileData)
        {
            ArgumentNullException.ThrowIfNull(fileData);

            return await GetFileStreamFromUrl(fileData.Url);
        }

        /// <summary>
        /// Gets image file as stream
        /// </summary>
        public async Task<Stream?> GetImageStream(string fileId, string? projectKey = null)
        {
            _logger.LogInformation("GetImageStream: Getting image stream for fileId={FileId}", fileId);
            return await GetPdfStream(fileId, projectKey); // Same logic
        }
    }
}

