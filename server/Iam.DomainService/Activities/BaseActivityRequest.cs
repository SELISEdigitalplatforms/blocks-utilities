using Blocks.Genesis;

namespace Iam.DomainService.Activities
{
    public class BaseActivityRequest : BaseGetsRequest<BaseActivityFilter>, IProjectKey
    {
        public string? ProjectKey { get; set; }
    }

    public class BaseActivityFilter
    {
        public string UserId { get; set; }
    }
}
