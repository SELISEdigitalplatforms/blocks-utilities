using System.Diagnostics;
using System.Text.Json;
using Api.Utilities;
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
    private static readonly JsonSerializerOptions WebJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IPaymentWebhookIntakeService _intake;
    private readonly IWebhookRequestBodyReader _bodyReader;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentWebhooksController> _logger;

    public PaymentWebhooksController(
        IPaymentWebhookIntakeService intake,
        IWebhookRequestBodyReader bodyReader,
        IHostApplicationLifetime applicationLifetime,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentWebhooksController> logger)
    {
        _intake = intake;
        _bodyReader = bodyReader;
        _applicationLifetime = applicationLifetime;
        _options = options;
        _logger = logger;
    }

    [HttpPost("standard")]
    [Consumes("application/json")]
    public async Task<IActionResult> Standard()
    {
        var stopwatch = Stopwatch.StartNew();
        var intakeId = Guid.NewGuid().ToString("N");

        using var scope = BeginWebhookScope(intakeId, "standard");

        var body = await ReadBodyAsync();

        if (body.Status != WebhookRequestBodyReadStatus.Success ||
            !TryDeserializeStandard(body.RawBody, out var request))
        {
            return RejectBody(body.Status, stopwatch);
        }

        _logger.LogInformation(
            "Webhook HTTP request received NotificationCount={NotificationCount} ContentLength={ContentLength} BodyBytes={BodyBytes}",
            request!.NotificationItems.Count,
            Request.ContentLength,
            System.Text.Encoding.UTF8.GetByteCount(
                body.RawBody));

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
    public async Task<IActionResult> Tokens()
    {
        var stopwatch = Stopwatch.StartNew();
        var intakeId = Guid.NewGuid().ToString("N");

        using var scope = BeginWebhookScope(intakeId, "token");

        var body = await ReadBodyAsync();

        if (body.Status != WebhookRequestBodyReadStatus.Success)
        {
            return RejectBody(body.Status, stopwatch);
        }

        var protocol = Request.Headers["protocol"].ToString();
        var signature = Request.Headers["hmacsignature"].ToString();

        if (!string.IsNullOrWhiteSpace(protocol) &&
            !protocol.Equals(
                "HmacSHA256",
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Webhook HTTP request rejected Reason=unsupported_signature_protocol DurationMs={DurationMs}",
                stopwatch.Elapsed.TotalMilliseconds);

            return Unauthorized();
        }

        _logger.LogInformation(
            "Webhook HTTP body accepted BodyBytes={BodyBytes} HasSignature={HasSignature}",
            System.Text.Encoding.UTF8.GetByteCount(body.RawBody),
            !string.IsNullOrWhiteSpace(signature));

        var outcome = await _intake.AcceptTokenAsync(
            body.RawBody,
            signature,
            _applicationLifetime.ApplicationStopping);

        _logger.LogInformation(
            "Webhook HTTP request completed Outcome={Outcome} StatusCode={StatusCode} DurationMs={DurationMs}",
            outcome,
            GetStatusCode(outcome),
            stopwatch.Elapsed.TotalMilliseconds);

        return Map(outcome);
    }

    private Task<WebhookRequestBodyReadResult> ReadBodyAsync()
    {
        var maximum = Math.Clamp(
            _options.CurrentValue.MaximumWebhookBodyBytes,
            16_384,
            1_048_576);

        _logger.LogInformation(
            "Webhook HTTP body read started ContentLength={ContentLength} MaximumBodyBytes={MaximumBodyBytes}",
            Request.ContentLength,
            maximum);

        return _bodyReader.ReadAsync(
            Request,
            maximum,
            _applicationLifetime.ApplicationStopping);
    }

    private IActionResult RejectBody(
        WebhookRequestBodyReadStatus status,
        Stopwatch stopwatch)
    {
        _logger.LogWarning(
            "Webhook HTTP request rejected Reason={Reason} ContentLength={ContentLength} DurationMs={DurationMs}",
            status == WebhookRequestBodyReadStatus.TooLarge
                ? "body_size_exceeded"
                : "malformed_or_incomplete_body",
            Request.ContentLength,
            stopwatch.Elapsed.TotalMilliseconds);

        return BadRequest();
    }

    private static bool TryDeserializeStandard(
        string rawBody,
        out StandardWebhookRequest? request)
    {
        try
        {
            request = JsonSerializer.Deserialize<StandardWebhookRequest>(
                rawBody,
                WebJsonOptions);

            return request != null;
        }
        catch (JsonException)
        {
            request = null;

            return false;
        }
    }

    private IActionResult Map(WebhookIntakeOutcome outcome) => outcome switch
    {
        WebhookIntakeOutcome.Accepted => Accepted(),
        WebhookIntakeOutcome.Unauthorized => Unauthorized(),
        WebhookIntakeOutcome.Malformed => BadRequest(),
        WebhookIntakeOutcome.NotFound => NotFound(),
        WebhookIntakeOutcome.StorageUnavailable =>
            StatusCode(
                StatusCodes.Status503ServiceUnavailable),
        _ =>
            StatusCode(
                StatusCodes.Status500InternalServerError)
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
        WebhookIntakeOutcome.StorageUnavailable =>
            StatusCodes.Status503ServiceUnavailable,
        _ =>
            StatusCodes.Status500InternalServerError
    };
}
