using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.DomainService.Requests;
using Sms.DomainService.Services;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("[controller]/[action]")]
public class SmsController : ControllerBase
{
    private readonly ISmsService _smsService;

    public SmsController(ISmsService smsService)
    {
        _smsService = smsService;
    }

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendSmsRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.SendAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost]
    public async Task<IActionResult> SendByTemplate([FromBody] SendSmsByTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.SendByTemplateAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost]
    public async Task<IActionResult> SaveProviderConfiguration([FromBody] SaveSmsProviderConfigurationRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.SaveProviderConfigurationAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetProviderConfiguration(CancellationToken cancellationToken)
    {
        var result = await _smsService.GetProviderConfigurationAsync(cancellationToken);
        return result.IsSuccess ? Ok(result) : NotFound(result);
    }
}
