using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.DomainService.Requests;
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
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SendByTemplate([FromBody] SendSmsByTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.SendByTemplateAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SaveProviderConfiguration([FromBody] SaveSmsProviderConfigurationRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.SaveProviderConfigurationAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
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
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost]
    public async Task<IActionResult> Telnyx([FromBody] TelnyxSmsStatusCallbackRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.ProcessTelnyxStatusAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }
}
