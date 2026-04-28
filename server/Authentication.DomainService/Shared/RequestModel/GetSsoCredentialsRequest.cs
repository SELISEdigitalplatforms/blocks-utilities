using Blocks.Genesis;

namespace DomainService.RequestModel
{
    public class GetSsoCredentialsRequest : IProjectKey
    {
        public string ProjectKey { get; set; }
    }
}
