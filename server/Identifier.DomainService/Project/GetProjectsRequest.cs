using Blocks.Genesis;

namespace DomainService.Projects
{
    public class GetProjectsRequest : BaseGetsRequest<GetProjectsFilter>
    {
        public string? TenantGroupId { get; set; }
    }
}
