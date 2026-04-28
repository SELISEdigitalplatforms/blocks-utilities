using Blocks.Genesis;

namespace DomainService.RequestModel
{
    public class GetSsoCredentialRequest : IProjectKey
    {
        public string ProjectKey { get; set; }
        public string ItemId { get; set; }
    }
}
