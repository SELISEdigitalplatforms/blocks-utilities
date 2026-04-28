using Blocks.Genesis;
using Cloud.DomainService.Requests;
using Cloud.DomainService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ApiEndpointConfigController : ControllerBase
    {
        private readonly IApiEndpointConfigService _service;

        public ApiEndpointConfigController(IApiEndpointConfigService service)
        {
            _service = service;
        }

   
        [HttpPost]
        public async Task<IActionResult> GetList([FromBody] GetApiEndpointConfigsRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectKey))
                return BadRequest(new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "missing_project_key", "ProjectKey is required" } }
                });

            var response = await _service.GetListAsync(request);
            return Ok(response);
        }

        [HttpPost]
        public async Task<IActionResult> Update([FromBody] UpdateApiEndpointConfigRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectKey))
                return BadRequest(new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "missing_project_key", "ProjectKey is required" } }
                });

            var response = await _service.UpdateAsync(request);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }

        [HttpPost]
        public async Task<IActionResult> BulkUpdate([FromBody] BulkUpdateApiEndpointConfigRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectKey))
                return BadRequest(new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "missing_project_key", "ProjectKey is required" } }
                });

            if (request.ItemIds == null || request.ItemIds.Count == 0)
                return BadRequest(new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "missing_item_ids", "At least one ItemId is required" } }
                });

            var response = await _service.BulkUpdateAsync(request);
            return response.IsSuccess ? Ok(response) : BadRequest(response);
        }
    }
}
