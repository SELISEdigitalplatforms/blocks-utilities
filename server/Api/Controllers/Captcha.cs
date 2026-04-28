using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.Captcha.ResponseModel;
using CloudConfiguration.DomainService.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class CaptchaController : ControllerBase
    {
        private readonly ICaptchaService _captchaService;
        private readonly IConfigurationService _configurationService;
        private readonly ChangeControllerContext _changeControllerContext;
        public CaptchaController(ICaptchaService captchaService, IConfigurationService configurationService, ChangeControllerContext changeControllerContext)
        {
            _captchaService = captchaService;
            _configurationService = configurationService;
            _changeControllerContext = changeControllerContext;

        }

        [Authorize]
        [HttpPost]
        public CreateCaptchaRequestResponse Create([FromBody] CreateCaptchaRequest command)
        {
            return _captchaService.CreateCaptcha(command);
        }

        [Authorize]
        [HttpPost]
        public Task<SubmitCaptchaRequestResponse> Submit([FromBody] SubmitCaptchaRequest command)
        {
            return _captchaService.SubmitCaptchaAsync(command);
        }

        [Authorize]
        [HttpGet]
        public Task<VerifyCaptchaRequestResponse> Verify([FromQuery] VerifyCaptchaRequest query)
        {
            return _captchaService.VerifyCaptchaAsync(query);
        }
        #region Cloud Configuration
        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseMutationResponse> Save([FromBody] SaveCaptchaConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.SaveCaptchaConfigurationAsync(request);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseMutationResponse> UpdateStatus([FromBody] UpdateCaptchaConfigurationStatusRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.UpdateCaptchaConfigurationStatusAsync(request);
        }

        [ProtectedEndPoint]
        [HttpGet]
        public async Task<BaseResponse> Get([FromQuery] GetCaptchaConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.GetCaptchaConfigurationAsync(request.ProviderName);
        }

        [ProtectedEndPoint]
        [HttpGet]
        public async Task<GetCaptchaConfigurationsResponse> Gets([FromQuery] GetCaptchaConfigurationsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.GetCaptchaConfigurationsAsync(request);
        }
        #endregion
    }
}
