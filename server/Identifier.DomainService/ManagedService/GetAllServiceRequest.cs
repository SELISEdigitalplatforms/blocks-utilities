using Blocks.Genesis;
using DomainService.Shared.Entities;

namespace DomainService.ManagedService
{
    public class GetAllServiceRequest : BaseGetsRequest<GetAllServiceFilter>, IProjectKey
    {
        public string ProjectKey { get; set; } 
    }


    public class GetAllServiceFilter
    {
        public string? ServiceId { get; set; }
        public string? ServiceName { get; set; }
    }

    public class GetAllServiceResponse : BaseQueryListResponse<IQueryable<BlocksManagedService>>
    {
        
    }

}
