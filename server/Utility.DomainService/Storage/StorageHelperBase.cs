using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.Storage
{
    /// <summary>
    /// Base class for storage helper operations - provides common functionality for file operations
    /// </summary>
    public abstract partial class StorageHelperBase
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
        protected Task<string?> ResolveParentDirectoryAsync(string logicalName, string? objectAccessLevel = null) =>
            _directories is null
                ? Task.FromResult<string?>(logicalName)
                : _directories.ResolveAsync(logicalName, objectAccessLevel);

        /// <summary>
        /// A driver response's own account of itself, for a log line.
        /// </summary>
        /// <remarks>
        /// The storage driver logs nothing and reports failure only through these two fields, so a
        /// caller that drops them leaves no trace of why anything failed: the upgrade to driver
        /// 4.1.2 refused every upload with <c>access=forbidden</c> and all that reached the log was
        /// "failed to get pre-signed URL".
        /// <para>
        /// Scrubbed like any caller-supplied value: an error's text can quote what was sent, the file
        /// name included, and a newline in it would forge a log entry (CWE-117).
        /// </para>
        /// </remarks>
        protected static string Describe(BaseResponse? response) =>
            LogSanitizer.Scrub(response is null
                ? "no response"
                : $"isSuccess={response.IsSuccess}, errors=[{(response.Errors is null or { Count: 0 }
                    ? "none"
                    : string.Join("; ", response.Errors.Select(error => $"{error.Key}={error.Value}")))}]");

        /// <summary>
        /// A failed upload's reason, as the provider gives it: its error code header plus a bounded,
        /// redacted slice of the body.
        /// </summary>
        /// <remarks>
        /// The body is the only place a rejected PUT explains itself (Azure and S3 both answer with
        /// XML), but it can quote the request that failed -- Azure's AuthenticationFailed detail
        /// carries the string-to-sign, S3 echoes query parameters -- so anything signature-shaped is
        /// masked before it reaches a log. Read bounded rather than whole: this is an untrusted
        /// response, and its length is the provider's choice, not ours.
        /// </remarks>
        protected static async Task<string> DescribeFailureAsync(HttpResponseMessage response)
        {
            const int Limit = 500;

            var codes = string.Join(
                ", ",
                new[] { "x-ms-error-code", "x-amz-request-id" }
                    .Where(header => response.Headers.Contains(header))
                    .Select(header => $"{header}={response.Headers.GetValues(header).FirstOrDefault()}"));

            try
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[Limit];
                var read = await stream.ReadAtLeastAsync(buffer, Limit, throwOnEndOfStream: false);
                var body = Redact(System.Text.Encoding.UTF8.GetString(buffer, 0, read));

                // The provider's body can echo the request, file name included: scrubbed (CWE-117).
                return LogSanitizer.Scrub(codes.Length == 0 ? body : $"{codes}; {body}");
            }
            catch (Exception exception)
            {
                return LogSanitizer.Scrub($"{codes}; unreadable: {exception.Message}");
            }
        }

        /// <summary>
        /// Masks the values of signature-shaped parameters anywhere in a string. Public so the tests
        /// that pin what must never be logged can call it directly.
        /// </summary>
        public static string Redact(string text) =>
            SignatureLikeValue().Replace(text, match => $"{match.Groups[1].Value}=<redacted>");

        /// <summary>A URL with its query string and fragment removed, for logging.</summary>
        public static string WithoutQuery(string url) =>
            LogSanitizer.Scrub(Uri.TryCreate(url, UriKind.Absolute, out var uri)
                ? uri.GetLeftPart(UriPartial.Path)
                : Redact(url));

        [System.Text.RegularExpressions.GeneratedRegex(
            """(sig|signature|x-amz-signature|x-amz-credential|awsaccesskeyid|skoid|sktid|sks|se|st|sp|sv)=([^&\s"'<]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
        private static partial System.Text.RegularExpressions.Regex SignatureLikeValue();

        /// <summary>
        /// Creates a storage <see cref="HttpClient"/> from the factory.
        /// </summary>
        /// <remarks>
        /// The factory owns the underlying handler and its connection pool, so the returned client
        /// is cheap to create per call and must not be disposed — that is the whole point of going
        /// through the factory rather than <c>new HttpClient()</c>, which leaks a socket pool per
        /// instance and exhausts ports under load.
        /// </remarks>
        protected HttpClient CreateHttpClient() =>
            _httpClientFactory.CreateClient(StorageHttpClientName);

        /// <summary>
        /// Downloads file content as stream from a URL
        /// </summary>
        protected async Task<Stream?> GetFileStreamFromUrl(string fileUrl)
        {
            // Never the whole URL: for a private file it is a signed link, and the query string is
            // the signature. Anyone reading the log could otherwise fetch the file themselves.
            _logger.LogInformation("GetFileStreamFromUrl: Downloading file from URL={FileUrl}", WithoutQuery(fileUrl));

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
