using Iam.DomainService.Entities;

namespace Iam.DomainService.Configurations
{
    public interface IIamConfigurationRepository
    {
        Task<bool> SaveConfigurationAsync(IamConfiguration iamConfiguration);
        Task<IamConfiguration> GetConfigurationAsync();
    }
}
