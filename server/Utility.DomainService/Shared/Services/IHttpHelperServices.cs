namespace Utility.DomainService.Shared.Services
{
    public interface IHttpHelperServices
    {
        Task<(T?, string)> MakeHttpRequest<T>(string clientName, string url, HttpMethod method, object? payload = null, Dictionary<string, string>? headers = null, string? token = null) where T : class;
        Task<(T?, string)> MakeHttpGetRequest<T>(string url, string token = null, Dictionary<string, string> headers = null) where T : class;
        Task<(T?, string)> MakeHttpPostRequest<T>(object payload, string url, Dictionary<string, string> headers = null, string token = null, string contentType = "application/json") where T : class;
    }
}

