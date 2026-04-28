using Blocks.Genesis;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;
using CloudConfiguration.DomainService.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class MailController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;
        private readonly ChangeControllerContext _changeControllerContext;
        public MailController(IConfigurationService configurationService, ChangeControllerContext changeControllerContext)
        {
            _configurationService = configurationService;
            _changeControllerContext = changeControllerContext;
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> Save([FromBody] MailConfiguration request)
        {
            _changeControllerContext.ChangeContext(request);

            if (string.IsNullOrWhiteSpace(request.ConfigurationId))
            {
                request.ConfigurationId = Guid.NewGuid().ToString();
            }

            var result = await _configurationService.SaveMailConfigurationAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<MailConfiguration> Get([FromQuery] GetMailConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _configurationService.GetMailConfigurationAsync(request);

            if (result == null)
            {
                var response = new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "Configuration", "No configuration found" }
                    }
                };

                BadRequest(response);
            }

            return result;
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<List<MailServerConfiguration>> Gets([FromQuery] GetAllMailConfigurationsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _configurationService.GetAllMailConfigurationsAsync();

            if (result == null)
            {
                var response = new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "Configuration", "No configuration found" }
                    }
                };

                BadRequest(response);
            }

            return result;
        }

        [HttpDelete]
        [ProtectedEndPoint]
        public async Task<IActionResult> Delete([FromQuery] DeleteMailConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);

            if (string.IsNullOrWhiteSpace(request.ConfigurationId))
            {
                return BadRequest(new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "ConfigurationId", "Invalid or missing ConfigurationId" }
                    }
                });
            }

            var result = await _configurationService.DeleteMailConfigurationAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> Duplicate([FromBody] DuplicateMailConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);

            if (string.IsNullOrWhiteSpace(request.ConfigurationId))
            {
                return BadRequest(new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "ConfigurationId", "Invalid or missing ConfigurationId" }
                    }
                });
            }

            var result = await _configurationService.DuplicateMailConfigurationAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }
    }
}
