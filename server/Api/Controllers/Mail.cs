using Api.Infrastructure;
using Blocks.Genesis;
using Mail.DomainService.Mails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class MailController : ControllerBase
    {
        private readonly IMailService _mailService;
        private readonly IChangeControllerContext _changeControllerContext;

        public MailController(IMailService mailService,
                              IChangeControllerContext changeControllerContext)
        {
            _mailService = mailService;
            _changeControllerContext = changeControllerContext;
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> SendToAny([FromBody] SendMailToAny request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _mailService.ProcessMailToAnyAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        public async Task<IActionResult> Send([FromBody] SendMail request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _mailService.ProcessMailAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetMailBoxMails([FromQuery] GetMailBoxMails request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _mailService.GetMailBoxMailsAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetMailBoxMail([FromQuery] GetMailBoxMail request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _mailService.GetMailBoxMailAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }
    }
}
