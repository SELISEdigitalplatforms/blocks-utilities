using Blocks.Genesis;
using DomainService.Shared.Entities;

namespace DomainService.Shared
{
    public interface IDomainManagementService
    {
        Task<BaseResponse> ConfigureDomainAsync(ConfigureDomainRequest request);
        Task<(bool, string)> DisableDomainBindingAsync(DisableDomainBindingRequest request);
    }
}
