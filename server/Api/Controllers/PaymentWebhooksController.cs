using System.Diagnostics;
using System.Text;
using BlocksTemplate.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Api.Controllers;

[ApiController]
[AllowAnonymous]
[SkipGlobalApiRoutePrefix]
[Route("payments/adyen/webhooks")]
public sealed class PaymentWebhooksController : ControllerBase
{
    private readonly IPaymentWebhookIntakeService _intake;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentWebhooksController> _logger;

    public PaymentWebhooksController(
        IPaymentWebhookIntakeService intake,
        IHostApplicationLifetime applicationLifetime,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentWebhooksController> logger)
    {
        _intake = intake;
        _applicationLifetime = applicationLifetime;
        _options = options;
        _logger = logger;
    }

    [HttpPost("standard")]
    public async Task<IActionResult> Standard([FromBody] StandardWebhookRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var intakeId = Guid.NewGuid().ToString("N");

        using var scope = BeginWebhookScope(intakeId, "standard");

        _logger.LogInformation(
            "Webhook HTTP request received NotificationCount={NotificationCount} ContentLength={ContentLength}",
            request.NotificationItems?.Count ?? 0,
            Request.ContentLength);

        var outcome = await _intake.AcceptStandardAsync(
            request,
            _applicationLifetime.ApplicationStopping);

        _logger.LogInformation(
            "Webhook HTTP request completed Outcome={Outcome} StatusCode={StatusCode} DurationMs={DurationMs}",
            outcome,
            GetStatusCode(outcome),
            stopwatch.Elapsed.TotalMilliseconds);

        return Map(outcome);
    }

    [HttpPost("tokens")]
    [Consumes("application/json")]
    public async Task<IActionResult> Tokens(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var intakeId = Guid.NewGuid().ToString("N");

        using var scope = BeginWebhookScope(intakeId, "token");

        var maximum = Math.Clamp(_options.CurrentValue.MaximumWebhookBodyBytes, 16_384, 1_048_576);

        _logger.LogInformation(
            "Webhook HTTP request received ContentLength={ContentLength} MaximumBodyBytes={MaximumBodyBytes}",
            Request.ContentLength,
            maximum);

        if (Request.ContentLength is > 0 && Request.ContentLength > maximum)
        {
            _logger.LogWarning(
                "Webhook HTTP request rejected Reason=content_length_exceeded DurationMs={DurationMs}",
                stopwatch.Elapsed.TotalMilliseconds);

            return BadRequest();
        }

        using var reader = new StreamReader(Request.Body, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        string rawBody;

        try
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Webhook HTTP body read cancelled Reason=request_aborted DurationMs={DurationMs}",
                stopwatch.Elapsed.TotalMilliseconds);

            throw;
        }

        var bodyBytes = Encoding.UTF8.GetByteCount(rawBody);
        if (bodyBytes > maximum)
        {
            _logger.LogWarning(
                "Webhook HTTP request rejected Reason=body_size_exceeded BodyBytes={BodyBytes} DurationMs={DurationMs}",
                bodyBytes,
                stopwatch.Elapsed.TotalMilliseconds);

            return BadRequest();
        }

        var protocol = Request.Headers["protocol"].ToString();
        var signature = Request.Headers["hmacsignature"].ToString();

        if (!string.IsNullOrWhiteSpace(protocol) && !protocol.Equals("HmacSHA256", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Webhook HTTP request rejected Reason=unsupported_signature_protocol DurationMs={DurationMs}",
                stopwatch.Elapsed.TotalMilliseconds);

            return Unauthorized();
        }

        _logger.LogInformation(
            "Webhook HTTP body accepted BodyBytes={BodyBytes} HasSignature={HasSignature}",
            bodyBytes,
            !string.IsNullOrWhiteSpace(signature));

        var outcome = await _intake.AcceptTokenAsync(
            rawBody,
            signature,
            _applicationLifetime.ApplicationStopping);

        _logger.LogInformation(
            "Webhook HTTP request completed Outcome={Outcome} StatusCode={StatusCode} DurationMs={DurationMs}",
            outcome,
            GetStatusCode(outcome),
            stopwatch.Elapsed.TotalMilliseconds);

        return Map(outcome);
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

    private IDisposable? BeginWebhookScope(
        string intakeId,
        string webhookType) =>
        _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WebhookIntakeId"] = intakeId,
            ["WebhookType"] = webhookType,
            ["TraceId"] = HttpContext.TraceIdentifier
        });

    private static int GetStatusCode(WebhookIntakeOutcome outcome) => outcome switch
    {
        WebhookIntakeOutcome.Accepted => StatusCodes.Status202Accepted,
        WebhookIntakeOutcome.Unauthorized => StatusCodes.Status401Unauthorized,
        WebhookIntakeOutcome.Malformed => StatusCodes.Status400BadRequest,
        WebhookIntakeOutcome.NotFound => StatusCodes.Status404NotFound,
        WebhookIntakeOutcome.StorageUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}
