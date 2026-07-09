using Blocks.Genesis;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Services.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class MailController : ControllerBase
    {
        private readonly IMailService _mailService;

        public MailController(IMailService mailService)
        {
            _mailService = mailService;
        }

        [HttpPost]
        // [ProtectedEndPoint("blocks-utilities::Mail::SendToAny")]
        [Authorize]
        public async Task<IActionResult> SendToAny([FromBody] SendMailToAny request)
        {
            var result = await _mailService.ProcessMailToAnyAsync(request);
            return ToMutationResult(result);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Send([FromBody] SendMail request)
        {
            var result = await _mailService.ProcessMailAsync(request);
            return ToMutationResult(result);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> GetEmailSends([FromBody] GetEmailSends request)
        {
            var result = await _mailService.GetEmailSendsAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetMailBoxMails([FromQuery] GetMailBoxMails request)
        {
            var result = await _mailService.GetMailBoxMailsAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetMailBoxMail([FromQuery] GetMailBoxMail request)
        {
            var result = await _mailService.GetMailBoxMailAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        private IActionResult ToMutationResult(BaseMutationResponse result)
        {
            if (result is MailMutationResponse { IsRateLimited: true } rateLimitedResult)
            {
                if (rateLimitedResult.RetryAfterSeconds.HasValue)
                {
                    Response.Headers.RetryAfter = rateLimitedResult.RetryAfterSeconds.Value.ToString();
                }

                return StatusCode(StatusCodes.Status429TooManyRequests, result);
            }

            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }
    }
}
