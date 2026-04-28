namespace Captcha.DomainService.Utilities
{
    public class HttpClientService : IHttpClientService
    {
        private readonly HttpClient _httpClient;

        public HttpClientService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string contentType)
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", contentType);
            return await _httpClient.SendAsync(request);
        }
    }
}
