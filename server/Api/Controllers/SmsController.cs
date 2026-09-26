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
    private readonly ISmsTemplateService _templates;

    public SmsController(ISmsService smsService, ISmsTemplateService templates)
    {
        _smsService = smsService;
        _templates = templates;
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

    /// <summary>Creates a template, or updates one when TemplateId is set. Name + language is unique per tenant.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveTemplate([FromBody] SaveSmsTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _templates.SaveAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetTemplate([FromQuery] string templateId, CancellationToken cancellationToken)
    {
        var result = await _templates.GetAsync(templateId, cancellationToken);
        return result.IsSuccess ? Ok(result) : NotFound(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetTemplates([FromQuery] GetSmsTemplatesRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _templates.ListAsync(request, cancellationToken));
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteTemplate([FromQuery] string templateId, CancellationToken cancellationToken)
    {
        var result = await _templates.DeleteAsync(templateId, cancellationToken);
        return result.IsSuccess ? Ok(result) : NotFound(result);
    }
}
