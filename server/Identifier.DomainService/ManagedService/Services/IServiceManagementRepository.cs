using DomainService.Shared.Entities;

namespace DomainService.ManagedService.Services
{
    public interface IServiceManagementRepository
    {
        Task SaveAsync(BlocksManagedService service);
        Task<(IQueryable<BlocksManagedService>, long)> GetAllServicesAsync(GetAllServiceRequest request);
    }
}
