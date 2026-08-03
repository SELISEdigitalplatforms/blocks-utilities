using Microsoft.AspNetCore.Http;

namespace Api.Utilities;

public interface IWebhookRequestBodyReader
{
    Task<WebhookRequestBodyReadResult> ReadAsync(
        HttpRequest request,
        int maximumBodyBytes,
        CancellationToken cancellationToken);
}
