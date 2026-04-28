namespace Captcha.DomainService.Utilities
{
    public interface IHttpClientService
    {
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string contentType);
    }
}
