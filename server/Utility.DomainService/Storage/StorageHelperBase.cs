using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace Utility.DomainService.Storage
{
    /// <summary>
    /// Base class for storage helper operations - provides common functionality for file operations
    /// </summary>
    public abstract class StorageHelperBase
    {
        protected readonly ILogger _logger;

        protected StorageHelperBase(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Downloads file content as stream from a URL
        /// </summary>
        protected async Task<Stream?> GetFileStreamFromUrl(string fileUrl)
        {
            _logger.LogInformation("GetFileStreamFromUrl: Downloading file from URL={FileUrl}", fileUrl);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Blocks-Key", BlocksContext.GetContext()?.TenantId);

            var response = await httpClient.GetAsync(fileUrl);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GetFileStreamFromUrl: Failed to download file, StatusCode={StatusCode}", response.StatusCode);
                return null;
            }

            var memoryStream = new MemoryStream();
            await response.Content.CopyToAsync(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);

            _logger.LogInformation("GetFileStreamFromUrl: Successfully downloaded file, size={Size} bytes", memoryStream.Length);
            return memoryStream;
        }
    }
}
