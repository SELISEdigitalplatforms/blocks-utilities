using Api.Infrastructure;
using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Template;
using Mail.DomainService.Template.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class TemplateController : ControllerBase
    {
        private readonly ITemplateService _templateService;
        private readonly IChangeControllerContext _changeControllerContext;

        public TemplateController(ITemplateService templateService, IChangeControllerContext changeControllerContext)
        {
            _templateService = templateService;
            _changeControllerContext = changeControllerContext;
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> Save([FromBody] Template template)
        {
            _changeControllerContext.ChangeContext(template);

            var result = await _templateService.SaveTemplateAsync(template);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<EmailTemplate?> Get([FromQuery] GetTemplate request)
        {
            _changeControllerContext.ChangeContext(request);

            var result = await _templateService.GetAsync(request);
            if (result == null)
            {
                var response = new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "Template", "No template found" }
                    }
                };

                BadRequest(response);
            }

            return result;
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<GetAllTemplatesResponse> Gets([FromQuery] GetAllTemplates request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _templateService.GetAllTemplatesAsync(request);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> Clone([FromBody] CloneTemplateRequest request)
        {
            _changeControllerContext.ChangeContext(request);

            var result = await _templateService.CloneTemplateAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpDelete]
        [ProtectedEndPoint]
        public async Task<IActionResult> Delete([FromQuery] DeleteTemplateRequest request)
        {
            if (request == null) BadRequest(new BaseMutationResponse());
            _changeControllerContext.ChangeContext(request);

            if (string.IsNullOrWhiteSpace(request.ItemId))
            {
                return BadRequest(new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "Invalid or missing itemId" }
                    }
                });
            }

            var result = await _templateService.DeleteAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }
    }
}
