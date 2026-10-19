using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Utility.DomainService.MagicLink.Utilities;

namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Service for executing HTTP requests for action-type magic links
    /// </summary>
    /// <remarks>
    /// Requests go through the <see cref="MagicLinkConstants.ActionHttpClientName"/> client, which
    /// only connects to public addresses, because the target URL is whatever the link's creator
    /// typed. A link that carries a client credential is further limited to the Blocks API host the
    /// token was minted by: the token holds that credential's roles, and the anonymous Invoke
    /// endpoint would otherwise let any link creator have it delivered to a server they control.
    /// </remarks>
    public class MagicLinkActionExecutor
    {
        public const string TokenHostMismatchMessage =
            "Links with a client credential may only target the Blocks API host that issues its token.";

        private readonly ILogger<MagicLinkActionExecutor> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public MagicLinkActionExecutor(
            ILogger<MagicLinkActionExecutor> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        /// <summary>
        /// True when the link's target has the same scheme, host and port as the token endpoint,
        /// so a client-credential token may be attached to the request.
        /// </summary>
        public bool MayReceiveToken(Models.MagicLink link)
        {
            ArgumentNullException.ThrowIfNull(link);

            return Uri.TryCreate(link.Uri, UriKind.Absolute, out var target) &&
                   Uri.TryCreate(ClientCredentialTokenService.ResolveTokenEndpoint(_configuration), UriKind.Absolute, out var issuer) &&
                   Uri.Compare(target, issuer, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>
        /// Execute the HTTP action mapped to a magic link
        /// </summary>
        /// <param name="link">The magic link to execute</param>
        /// <param name="token">Optional authentication token</param>
        /// <returns>Result of the action execution</returns>
        public async Task<MagicLinkActionExecutionResult> ExecuteActionAsync(Models.MagicLink link, string? token = null)
        {
            try
            {
                if (string.IsNullOrEmpty(link.RequestMethod))
                {
                    return new MagicLinkActionExecutionResult
                    {
                        IsSuccess = false,
                        StatusCode = 400,
                        ErrorMessage = "RequestMethod is required for Action type links"
                    };
                }

                if (!string.IsNullOrEmpty(token) && !MayReceiveToken(link))
                {
                    _logger.LogWarning("Refusing to send a client-credential token off the Blocks API host for LinkId: {LinkId}", link.ItemId);
                    return new MagicLinkActionExecutionResult
                    {
                        IsSuccess = false,
                        StatusCode = 403,
                        ErrorMessage = TokenHostMismatchMessage
                    };
                }

                // Host only: the full URL often carries tokens or personal data in its query string
                _logger.LogInformation("Executing action for LinkId: {LinkId}, Method: {Method}, Host: {Host}",
                    link.ItemId, link.RequestMethod, Uri.TryCreate(link.Uri, UriKind.Absolute, out var target) ? target.Host : null);

                return link.RequestMethod.ToUpperInvariant() switch
                {
                    "GET" => await ExecuteGetActionAsync(link, token),
                    "POST" => await ExecutePostActionAsync(link, token),
                    "PUT" => await ExecutePutActionAsync(link, token),
                    "DELETE" => await ExecuteDeleteActionAsync(link, token),
                    _ => new MagicLinkActionExecutionResult
                    {
                        IsSuccess = false,
                        StatusCode = 400,
                        ErrorMessage = $"Unsupported HTTP method: {link.RequestMethod}"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing action for LinkId: {LinkId}", link.ItemId);
                return new MagicLinkActionExecutionResult
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    ErrorMessage = $"Error executing action: {ex.Message}"
                };
            }
        }

        private async Task<MagicLinkActionExecutionResult> ExecuteGetActionAsync(Models.MagicLink link, string? token)
        {
            using var client = _httpClientFactory.CreateClient(MagicLinkConstants.ActionHttpClientName);
            var apiUrl = BuildApiUrl(link);

            SetupClient(client, link, token);

            var response = await client.GetAsync(apiUrl);
            return await BuildResultAsync(response);
        }

        private async Task<MagicLinkActionExecutionResult> ExecutePostActionAsync(Models.MagicLink link, string? token)
        {
            using var client = _httpClientFactory.CreateClient(MagicLinkConstants.ActionHttpClientName);
            var apiUrl = BuildApiUrl(link);

            SetupClient(client, link, token);

            HttpContent? content = null;
            if (!string.IsNullOrEmpty(link.RequestPayload))
            {
                content = new StringContent(link.RequestPayload, Encoding.UTF8, "application/json");
            }

            var response = await client.PostAsync(apiUrl, content);
            return await BuildResultAsync(response);
        }

        private async Task<MagicLinkActionExecutionResult> ExecutePutActionAsync(Models.MagicLink link, string? token)
        {
            using var client = _httpClientFactory.CreateClient(MagicLinkConstants.ActionHttpClientName);
            var apiUrl = BuildApiUrl(link);

            SetupClient(client, link, token);

            HttpContent? content = null;
            if (!string.IsNullOrEmpty(link.RequestPayload))
            {
                content = new StringContent(link.RequestPayload, Encoding.UTF8, "application/json");
            }

            var response = await client.PutAsync(apiUrl, content);
            return await BuildResultAsync(response);
        }

        private async Task<MagicLinkActionExecutionResult> ExecuteDeleteActionAsync(Models.MagicLink link, string? token)
        {
            using var client = _httpClientFactory.CreateClient(MagicLinkConstants.ActionHttpClientName);
            var apiUrl = BuildApiUrl(link);

            SetupClient(client, link, token);

            var response = await client.DeleteAsync(apiUrl);
            return await BuildResultAsync(response);
        }

        private static string BuildApiUrl(Models.MagicLink link)
        {
            var url = link.Uri;

            if (!string.IsNullOrEmpty(link.RequestEncodedQueryString))
            {
                var separator = url.Contains('?') ? "&" : "?";
                url = $"{url}{separator}{link.RequestEncodedQueryString}";
            }

            return url;
        }

        private void SetupClient(HttpClient client, Models.MagicLink link, string? token)
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                _logger.LogWarning("Executing action without authentication token for LinkId: {LinkId}.", link.ItemId);
            }

            // Add extra headers from link
            if (!string.IsNullOrEmpty(link.RequestHeaders))
            {
                try
                {
                    var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(link.RequestHeaders);
                    if (headers != null)
                    {
                        foreach (var header in headers)
                        {
                            // Host would reroute the request at the gateway; a stored Authorization
                            // would ride alongside (or replace) the minted token.
                            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(token) && string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            if (!string.IsNullOrEmpty(header.Key) && !string.IsNullOrEmpty(header.Value))
                            {
                                client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse RequestHeaders JSON for LinkId: {LinkId}", link.ItemId);
                }
            }
        }

        private async Task<MagicLinkActionExecutionResult> BuildResultAsync(HttpResponseMessage response)
        {
            var statusCode = (int)response.StatusCode;
            var isSuccess = response.IsSuccessStatusCode;

            object? resultData = null;
            try
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(responseContent))
                {
                    resultData = JsonSerializer.Deserialize<object>(responseContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize response content");
            }

            return new MagicLinkActionExecutionResult
            {
                IsSuccess = isSuccess,
                StatusCode = statusCode,
                Data = resultData,
                ErrorMessage = isSuccess ? null : $"HTTP {statusCode}"
            };
        }
    }

    /// <summary>
    /// Result of magic link action execution
    /// </summary>
    public class MagicLinkActionExecutionResult
    {
        public bool IsSuccess { get; set; }
        public int StatusCode { get; set; }
        public object? Data { get; set; }
        public string? ErrorMessage { get; set; }
    }
}

