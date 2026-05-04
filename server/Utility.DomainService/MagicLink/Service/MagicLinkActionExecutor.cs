using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Service for executing HTTP requests for action-type magic links
    /// </summary>
    public class MagicLinkActionExecutor
    {
        private readonly ILogger<MagicLinkActionExecutor> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public MagicLinkActionExecutor(
            ILogger<MagicLinkActionExecutor> logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
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

                _logger.LogInformation("Executing action for LinkId: {LinkId}, Method: {Method}, Url: {Url}",
                    link.ItemId, link.RequestMethod, link.Uri);

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
            using var client = _httpClientFactory.CreateClient();
            var apiUrl = BuildApiUrl(link);

            SetupClient(client, link, token);

            var response = await client.GetAsync(apiUrl);
            return await BuildResultAsync(response);
        }

        private async Task<MagicLinkActionExecutionResult> ExecutePostActionAsync(Models.MagicLink link, string? token)
        {
            using var client = _httpClientFactory.CreateClient();
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
            using var client = _httpClientFactory.CreateClient();
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
            using var client = _httpClientFactory.CreateClient();
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

