using Blocks.Genesis;
using DomainService.Entities;

namespace DomainService.Configuration.Services
{
    public interface IConfigurationService
    {
        Task<BaseResponse> SaveConfigurationAsync(SaveConfigurationRequest configuration);
        Task<GetConfigurationsResponse> GetsAsync(GetConfigurationsRequest request);
        Task<NotificationConfiguration> GetAsync(GetConfigurationRequest request);
        Task<BaseResponse> DeleteAsync(DeleteConfigurationRequest request);
    }
}
