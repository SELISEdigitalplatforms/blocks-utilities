namespace DomainService.ManagedService.Services
{
    public interface IServiceManagement
    {
        Task<RegisterServiceResponse> RegisterServiceAsync(RegisterServiceRequest request);
        Task<GetAllServiceResponse> GetAllServicesAsync(GetAllServiceRequest request);
    }
}
