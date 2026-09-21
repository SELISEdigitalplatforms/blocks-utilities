using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace Utility.DomainService.Storage
{
    /// <summary>
    /// Base class for storage helper operations - provides common functionality for file operations
    /// </summary>
    public abstract class StorageHelperBase
    {
        /// <summary>
        /// Name of the pooled <see cref="HttpClient"/> every storage helper uploads and downloads
        /// through.
        /// </summary>
        /// <remarks>
        /// Named rather than the default client so a handler policy (timeout, retry) can later be
        /// attached to storage traffic alone, without touching the other clients in the host.
        /// </remarks>
        public const string StorageHttpClientName = "utility-storage";

        protected readonly ILogger _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly StorageDirectoryResolver? _directories;

        protected StorageHelperBase(
            ILogger logger,
            IHttpClientFactory httpClientFactory,
            StorageDirectoryResolver? directories = null)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _directories = directories;
        }

        /// <summary>
        /// The real parent directory id for a logical directory name -- see
        /// <see cref="StorageDirectoryResolver"/>. Passed through unchanged when no resolver was
        /// supplied, which only a hand-constructed helper lacks; the container always supplies one.
        /// </summary>
        protected Task<string?> ResolveParentDirectoryAsync(string logicalName) =>
            _directories is null
                ? Task.FromResult<string?>(logicalName)
                : _directories.ResolveAsync(logicalName);

        /// <summary>
        /// Creates a storage <see cref="HttpClient"/> from the factory.
        /// </summary>
        /// <remarks>
        /// The factory owns the underlying handler and its connection pool, so the returned client
        /// is cheap to create per call and must not be disposed — that is the whole point of going
        /// through the factory rather than <c>new HttpClient()</c>, which leaks a socket pool per
        /// instance and exhausts ports under load.
        /// </remarks>
        /// <summary>
        /// A driver response's own account of itself, for a log line.
        /// </summary>
        /// <remarks>
        /// The storage driver logs nothing and reports failure only through these two fields, so a
        /// caller that drops them leaves no trace of why anything failed: the upgrade to driver
        /// 4.1.2 refused every upload with <c>access=forbidden</c> and all that reached the log was
        /// "failed to get pre-signed URL".
        /// </remarks>
        protected static string Describe(BaseResponse? response) =>
            response is null
                ? "no response"
                : $"isSuccess={response.IsSuccess}, errors=[{(response.Errors is null or { Count: 0 }
                    ? "none"
                    : string.Join("; ", response.Errors.Select(error => $"{error.Key}={error.Value}")))}]";

        /// <summary>
        /// A failed upload's response body, trimmed: the provider explains a rejected PUT there
        /// (Azure and S3 both answer with XML) and nothing else carries that reason.
        /// </summary>
        protected static async Task<string> DescribeFailureAsync(HttpResponseMessage response)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();

                return body.Length <= 500 ? body : body[..500];
            }
            catch (Exception exception)
            {
                return $"unreadable: {exception.Message}";
            }
        }

        protected HttpClient CreateHttpClient() =>
            _httpClientFactory.CreateClient(StorageHttpClientName);

        /// <summary>
        /// Downloads file content as stream from a URL
        /// </summary>
        protected async Task<Stream?> GetFileStreamFromUrl(string fileUrl)
        {
            _logger.LogInformation("GetFileStreamFromUrl: Downloading file from URL={FileUrl}", fileUrl);

            var httpClient = CreateHttpClient();

            // Set on the request, not on DefaultRequestHeaders: the tenant comes from the ambient
            // context and differs call to call, so it must never outlive this one request.
            using var request = new HttpRequestMessage(HttpMethod.Get, fileUrl);
            request.Headers.Add("X-Blocks-Key", BlocksContext.GetContext()?.TenantId);

            var response = await httpClient.SendAsync(request);

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
