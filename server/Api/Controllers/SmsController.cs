using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.DomainService.Requests;
using Sms.DomainService.Responses;
using Sms.DomainService.Services;

namespace Api.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class SmsController : ControllerBase
{
    private readonly ISmsService _smsService;

    public SmsController(ISmsService smsService)
    {
        _smsService = smsService;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Send([FromBody] SendSmsRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.SendAsync(request, cancellationToken);
        return result.IsSuccess ? Accepted(result) : ToFailureResult(result);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SendByTemplate([FromBody] SendSmsByTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.SendByTemplateAsync(request, cancellationToken);
        return result.IsSuccess ? Accepted(result) : ToFailureResult(result);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SaveProviderConfiguration([FromBody] SaveSmsProviderConfigurationRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.SaveProviderConfigurationAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ToFailureResult(result);
        }

        return string.IsNullOrWhiteSpace(request.ConfigurationId)
            ? CreatedAtAction(nameof(GetProviderConfiguration), new { projectKey = request.ProjectKey }, result)
            : Ok(result);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetProviderConfiguration([FromQuery] string? projectKey, CancellationToken cancellationToken)
    {
        var result = await _smsService.GetProviderConfigurationAsync(projectKey, cancellationToken);
        return result.IsSuccess ? Ok(result) : NotFound(result);
    }

    [HttpPost]
    public async Task<IActionResult> Twilio([FromForm] TwilioSmsStatusCallbackRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.ProcessTwilioStatusAsync(request, cancellationToken);
        return result.IsSuccess ? NoContent() : ToFailureResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> Telnyx([FromBody] TelnyxSmsStatusCallbackRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.ProcessTelnyxStatusAsync(request, cancellationToken);
        return result.IsSuccess ? NoContent() : ToFailureResult(result);
    }

    private IActionResult ToFailureResult(SmsMutationResponse result)
    {
        if (result.Errors.ContainsKey("RateLimit"))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, result);
        }

        if (result.Errors.ContainsKey("Security"))
        {
            return UnprocessableEntity(result);
        }

        if (result.Errors.ContainsKey("Queue") || result.Errors.ContainsKey("Configuration"))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result);
        }

        if (result.Errors.TryGetValue("TemplateName", out var templateError) &&
            templateError.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(result);
        }

        if (result.Errors.TryGetValue("ProviderMessageId", out var providerMessageIdError))
        {
            return providerMessageIdError.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(result)
                : BadRequest(result);
        }

        return BadRequest(result);
    }
}
