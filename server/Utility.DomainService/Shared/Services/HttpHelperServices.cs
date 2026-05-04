using Blocks.Genesis;
using System.Text;
using System.Text.Json;

namespace Utility.DomainService.Shared.Services
{
    public class HttpHelperServices : IHttpHelperServices
    {
        private readonly IHttpService _httpService;
        private readonly IHttpClientFactory _httpClientFactory;

        public HttpHelperServices(IHttpService httpService, IHttpClientFactory httpClientFactory)
        {
            _httpService = httpService;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<(T?, string)> MakeHttpGetRequest<T>(string url, string token = null, Dictionary<string, string> headers = null) where T : class
        {
            try
            {
                Console.WriteLine($"Making GET request to: {url}");
                var (data, rawResponse) = await _httpService.Get<T>(url, headers);
                return (data, rawResponse);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error making GET request to: {url}, Exception: {ex.Message}");
                return (null, "Operation Failed.");
            }
        }

        public async Task<(T?, string)> MakeHttpPostRequest<T>(object payload, string url, Dictionary<string, string> headers = null, string token = null, string contentType = "application/json") where T : class
        {
            try
            {
                var (data, rawResponse) = await _httpService.Post<T>(payload, url, contentType, headers);
                return (data, rawResponse);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error making POST request to: {url}, Exception: {ex.Message}");
                return (null, "Operation Failed.");
            }
        }

        public async Task<(T?, string)> MakeHttpRequest<T>(string clientName, string url, HttpMethod method, object? payload = null, Dictionary<string, string>? headers = null, string? token = null) where T : class
        {
            try
            {
                Console.WriteLine($"Making {method.Method} request to: {url}");

                var client = _httpClientFactory.CreateClient(clientName);

                // Add Authorization header if token is provided
                if (!string.IsNullOrEmpty(token))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }

                // Add any additional headers
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                HttpRequestMessage request = new HttpRequestMessage(method, url);

                // Add payload if method is POST or PUT
                if ((method == HttpMethod.Post || method == HttpMethod.Put) && payload != null)
                {
                    string jsonPayload = JsonSerializer.Serialize(payload);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                }

                var response = await client.SendAsync(request);
                var contentString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Request failed with status code: {response.StatusCode}, Body: {contentString}");
                    return (null, "Operation Failed.");
                }

                var deserialized = JsonSerializer.Deserialize<T>(contentString);
                return (deserialized, "Success");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error making {method.Method} request to: {url}, Exception: {ex.Message}");
                return (null, "Operation Failed.");
            }
        }
    }
}

