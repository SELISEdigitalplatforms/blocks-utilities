
using Blocks.Genesis;

namespace DomainService.Projects
{
    public class RestoreProjectRequest
    {
        public string? ProjectId { get; set; }
    }

    public class RestoreProjectResponse: BaseResponse
    {
        public string? ItemId { get; set; }
    }
}
