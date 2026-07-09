using Mail.DomainService.Mails.Services.DeliveryTracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;

namespace Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/mail/providers/ses/events")]
public sealed class AmazonSesEventsController : ControllerBase
{
    private const int MaxPayloadCharacters = 1024 * 1024;
    private readonly ISesNotificationService _notificationService;

    public AmazonSesEventsController(ISesNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpPost]
    [DisableRateLimiting]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > 1024 * 1024)
        {
            return BadRequest();
        }

        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxPayloadCharacters)
        {
            return BadRequest();
        }

        SesNotificationResult result;
        try
        {
            result = await _notificationService.ProcessAsync(payload, cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        return result.Outcome switch
        {
            SesNotificationOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            SesNotificationOutcome.Invalid => BadRequest(),
            _ => Ok()
        };
    }
}
