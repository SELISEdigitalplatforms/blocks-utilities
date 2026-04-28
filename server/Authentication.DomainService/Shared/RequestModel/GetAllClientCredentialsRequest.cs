using Blocks.Genesis;

namespace DomainService.Shared.RequestModel
{
    public class GetAllClientCredentialsRequest : IProjectKey
    {
        public string ProjectKey { get ; set ; }
    }
}
