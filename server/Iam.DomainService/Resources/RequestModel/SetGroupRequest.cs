using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class SetGroupRequest : IProjectKey
    {
        public List<string> Permissions { get; set; } = new List<string>();
        public string Slug { get; set; }
        public string ProjectKey { get; set; }
    }

    public class SetGroupResponse
    {
        public bool Success { get; set; }
    }
}
