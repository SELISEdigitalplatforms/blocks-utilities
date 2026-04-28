using Blocks.Genesis;

namespace DomainService.Projects
{
    public class GetTokenValidationParametersRequest : IProjectKey
    {
        public string ProjectKey { get; set; }
    }
}
