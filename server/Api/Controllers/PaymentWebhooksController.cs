using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("payments/adyen/webhooks/{tenantId}")]
public sealed class PaymentWebhooksController : ControllerBase
{
    private readonly IPaymentWebhookIntakeService _intake;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    public PaymentWebhooksController(IPaymentWebhookIntakeService intake, IOptionsMonitor<PaymentOptions> options)
    {
        _intake = intake;
        _options = options;
    }

    [HttpPost("standard")]
    public async Task<IActionResult> Standard(
        string tenantId,
        [FromBody] StandardWebhookRequest request,
        CancellationToken cancellationToken) =>
        Map(await _intake.AcceptStandardAsync(tenantId, request, cancellationToken));

    [HttpPost("tokens")]
    [Consumes("application/json")]
    public async Task<IActionResult> Tokens(string tenantId, CancellationToken cancellationToken)
    {
        var maximum = Math.Clamp(_options.CurrentValue.MaximumWebhookBodyBytes, 16_384, 1_048_576);
        if (Request.ContentLength is > 0 && Request.ContentLength > maximum) return BadRequest();
        using var reader = new StreamReader(Request.Body, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(rawBody) > maximum) return BadRequest();
        var protocol = Request.Headers["protocol"].ToString();
        var signature = Request.Headers["hmacsignature"].ToString();
        if (!string.IsNullOrWhiteSpace(protocol) && !protocol.Equals("HmacSHA256", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();
        return Map(await _intake.AcceptTokenAsync(tenantId, rawBody, signature, cancellationToken));
    }

    private IActionResult Map(WebhookIntakeOutcome outcome) => outcome switch
    {
        WebhookIntakeOutcome.Accepted => Accepted(),
        WebhookIntakeOutcome.Unauthorized => Unauthorized(),
        WebhookIntakeOutcome.Malformed => BadRequest(),
        WebhookIntakeOutcome.NotFound => NotFound(),
        WebhookIntakeOutcome.StorageUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };
}
