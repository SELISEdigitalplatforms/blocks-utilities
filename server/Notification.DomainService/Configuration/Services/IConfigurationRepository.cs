using Blocks.Genesis;
using DomainService.Entities;

namespace DomainService.Configuration.Services
{
    public interface IConfigurationRepository
    {
        Task SaveAsync(NotificationConfiguration configuration);
        Task<NotificationConfiguration> GetByNameAsync(string name);
        Task<NotificationConfiguration> GetByIdAsync(string id);
        Task<GetConfigurationsResponse> GetConfigurationsAsync(GetConfigurationsRequest request);
        Task<BaseResponse> DeleteConfigurationAsync(DeleteConfigurationRequest request);
    }
}
