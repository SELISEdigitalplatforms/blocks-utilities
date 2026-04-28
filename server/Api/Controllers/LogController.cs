using Blocks.Genesis;
using Cloud.LmtService.Models.Logs;
using Cloud.LmtService.Services.Logs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class LogController : ControllerBase
    {
        private readonly ILogService _logService;
        private readonly ChangeControllerContext _changeControllerContext;

        public LogController(
            ILogService logService,
            ChangeControllerContext changeControllerContext)
        {
            _logService = logService;
            _changeControllerContext = changeControllerContext;
        }


        [HttpPost]
     
        public async Task<IActionResult> GetLogs([FromBody] GetLogsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _logService.GetLogsAsync(request);
            return Ok(result);
        }

        [HttpPost]
        public async Task<GetLogsResponse> GetLogsByDate([FromBody] LogsByDateRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _logService.GetLogsByDateAsync(request);
        }


        [HttpGet]
        public async Task<IActionResult> Live([FromQuery] LiveLogRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            var result = await _logService.GetLiveLogsAsync(request);
            return Ok(result);
        }

    }
}
