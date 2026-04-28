using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class StorageController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;
        private readonly ChangeControllerContext _changeControllerContext;

        public StorageController(IConfigurationService configurationService,
                                 ChangeControllerContext changeControllerContext)
        {
            _configurationService = configurationService;
            _changeControllerContext = changeControllerContext;
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<BaseMutationResponse> Save([FromBody] SaveStorageConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.SaveStorageConfigurationAsync(request);
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<List<StorageConfiguration>> Gets([FromQuery] GetStorageConfigurationsRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.GetStorageConfigurationsAsync();
        }

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<StorageConfiguration> Get([FromQuery] GetStorageConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.GetStorageConfigurationAsync(request?.ConfigurationName ?? string.Empty);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<BaseResponse> Delete([FromQuery] DeleteStorageConfigurationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _configurationService.DeleteStorageConfigurationAsync(request?.ConfigurationName ?? string.Empty);
        }

    }
}
