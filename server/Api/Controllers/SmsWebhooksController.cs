using Api.Utilities;
using BlocksTemplate.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.DomainService.Dtos;
using Sms.DomainService.Services;

namespace Api.Controllers;

/// <summary>
/// Provider delivery callbacks, laid out like <see cref="PaymentWebhooksController"/>: anonymous,
/// outside the global <c>api</c> prefix, raw body read once so the signature is checked over the
/// exact bytes the provider signed. The URL is built by <c>SmsCallbackUrls</c>.
/// </summary>
[ApiController]
[AllowAnonymous]
[SkipGlobalApiRoutePrefix]
[Route("sms")]
public sealed class SmsWebhooksController : ControllerBase
{
    private const int MaximumBodyBytes = 65_536;

    private readonly ISmsWebhookService _webhooks;
    private readonly IWebhookRequestBodyReader _bodyReader;
    private readonly IHostApplicationLifetime _applicationLifetime;

    public SmsWebhooksController(
        ISmsWebhookService webhooks,
        IWebhookRequestBodyReader bodyReader,
        IHostApplicationLifetime applicationLifetime)
    {
        _webhooks = webhooks;
        _bodyReader = bodyReader;
        _applicationLifetime = applicationLifetime;
    }

    [HttpPost("{provider}/webhooks/{tenantId}")]
    public async Task<IActionResult> Provider(string provider, string tenantId)
    {
        var body = await _bodyReader.ReadAsync(Request, MaximumBodyBytes, _applicationLifetime.ApplicationStopping);
        if (body.Status != WebhookRequestBodyReadStatus.Success)
        {
            return BadRequest();
        }

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var outcome = await _webhooks.HandleAsync(provider, tenantId, new SmsWebhookRequest(body.RawBody, headers), _applicationLifetime.ApplicationStopping);

        return outcome switch
        {
            SmsWebhookOutcome.Accepted => Ok(),
            SmsWebhookOutcome.Unauthorized => Unauthorized(),
            SmsWebhookOutcome.Malformed => BadRequest(),
            _ => NotFound()
        };
    }
}
