using Blocks.Genesis;

namespace DomainService.Projects
{
    public class UpdateProjectRequest : IProjectKey
    {
        public string? CustomDomain { get; set; }
        public string ApplicationDomain { get; set; }
        public string ProjectKey { get ; set ; }
    }
}
