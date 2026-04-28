using Blocks.Genesis;
using DomainService.Entities;


namespace DomainService.Projects
{
    public class GetProjectRequest
    {
        public string ProjectId { get; set; }
    }

    public class GetProjectResponse : BaseQueryResponse<GetProjectResponseData>
    {

    }
    public class GetProjectResponseData : Project
    {
        public string TenantSlug { get; set; }
    }
}
