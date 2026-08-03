using System.Diagnostics;
using Api.Utilities;
using BlocksTemplate.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Payment.DomainService.Providers;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Api.Controllers;

[ApiController]
[AllowAnonymous]
[SkipGlobalApiRoutePrefix]
[Route("payments")]
public sealed class PaymentWebhooksController : ControllerBase
{
    private readonly IPaymentWebhookIntakeService _intake;
    private readonly IPaymentProviderCatalog _providerCatalog;
    private readonly IWebhookRequestBodyReader _bodyReader;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentWebhooksController> _logger;

    public PaymentWebhooksController(
        IPaymentWebhookIntakeService intake,
        IPaymentProviderCatalog providerCatalog,
        IWebhookRequestBodyReader bodyReader,
        IHostApplicationLifetime applicationLifetime,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentWebhooksController> logger)
    {
        _intake = intake;
        _providerCatalog = providerCatalog;
        _bodyReader = bodyReader;
        _applicationLifetime = applicationLifetime;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Adyen's originally published endpoints. Both webhook kinds are told apart by the
    /// normalizer from the body itself, so they share one handler.
    /// </summary>
    [HttpPost("adyen/webhooks/standard")]
    [HttpPost("adyen/webhooks/tokens")]
    [Consumes("application/json")]
    public Task<IActionResult> Adyen() =>
        HandleAsync(PaymentConstants.AdyenOnlineProvider);

    [HttpPost("{provider}/webhooks")]
    [Consumes("application/json")]
    public Task<IActionResult> Provider(string provider) =>
        HandleAsync(provider);

    private async Task<IActionResult> HandleAsync(string providerName)
    {
        var stopwatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WebhookIntakeId"] = Guid.NewGuid().ToString("N"),
            ["Provider"] = PaymentLogValue.Label(providerName),
            ["TraceId"] = HttpContext.TraceIdentifier
        });

        if (!_providerCatalog.IsRegistered(providerName))
        {
            _logger.LogWarning("Webhook HTTP request rejected Reason=provider_not_registered");

            return NotFound();
        }

        var body = await ReadBodyAsync();

        if (body.Status != WebhookRequestBodyReadStatus.Success)
        {
            _logger.LogWarning(
                "Webhook HTTP request rejected Reason={Reason} ContentLength={ContentLength} DurationMs={DurationMs}",
                body.Status == WebhookRequestBodyReadStatus.TooLarge
                    ? "body_size_exceeded"
                    : "malformed_or_incomplete_body",
                Request.ContentLength,
                stopwatch.Elapsed.TotalMilliseconds);

            return BadRequest();
        }

        var outcome = await _intake.AcceptAsync(
            providerName,
            body.RawBody,
            ReadHeaders(),
            _applicationLifetime.ApplicationStopping);

        _logger.LogInformation(
            "Webhook HTTP request completed Outcome={Outcome} DurationMs={DurationMs}",
            outcome,
            stopwatch.Elapsed.TotalMilliseconds);

        return Map(outcome);
    }

    private IReadOnlyDictionary<string, string> ReadHeaders() =>
        Request.Headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

    private Task<WebhookRequestBodyReadResult> ReadBodyAsync() =>
        _bodyReader.ReadAsync(
            Request,
            Math.Clamp(_options.CurrentValue.MaximumWebhookBodyBytes, 16_384, 1_048_576),
            _applicationLifetime.ApplicationStopping);

    private IActionResult Map(WebhookIntakeOutcome outcome) => outcome switch
    {
        WebhookIntakeOutcome.Accepted => Accepted(),
        WebhookIntakeOutcome.Unauthorized => Unauthorized(),
        WebhookIntakeOutcome.Malformed => BadRequest(),
        WebhookIntakeOutcome.NotFound => NotFound(),
        WebhookIntakeOutcome.StorageUnavailable =>
            StatusCode(StatusCodes.Status503ServiceUnavailable),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };
}
