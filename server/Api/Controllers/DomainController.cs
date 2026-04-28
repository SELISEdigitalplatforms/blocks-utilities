using Blocks.Genesis;
using DomainService.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class DomainController : ControllerBase
    {
        private readonly IDomainManagementService _domainManagementService;

        public DomainController(IDomainManagementService domainManagementService)
        {
            _domainManagementService = domainManagementService;
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseResponse> Configure([FromBody] ConfigureDomainRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CookieDomain) || string.IsNullOrWhiteSpace(request.ProjectKey))
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "missing_required_fields", "ProjectKey or domain name is missing" } } };
            }

            return await _domainManagementService.ConfigureDomainAsync(request);
        }
    }
}
