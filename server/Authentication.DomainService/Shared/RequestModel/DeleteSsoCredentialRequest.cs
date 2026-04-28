using Blocks.Genesis;

namespace DomainService.RequestModel
{
    public class DeleteSsoCredentialRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public string ProjectKey { get; set; }
    }
}
