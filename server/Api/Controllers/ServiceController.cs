using DomainService.ManagedService;
using DomainService.ManagedService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Blocks.Genesis;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ServiceController : ControllerBase
    {
        private readonly IServiceManagement _serviceManagement;

        public ServiceController(IServiceManagement serviceManagement)
        {
            _serviceManagement = serviceManagement;
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<IActionResult> Register([FromBody] RegisterServiceRequest request)
        {
            var response = await _serviceManagement.RegisterServiceAsync(request);

            if (response.IsSuccess)
            {
                return Ok(response);
            }
            return BadRequest(response);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<GetAllServiceResponse> GetAll([FromBody] GetAllServiceRequest request)
        {
            return await _serviceManagement.GetAllServicesAsync(request);
        }
    }
}
