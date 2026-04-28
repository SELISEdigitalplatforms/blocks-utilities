using Blocks.Genesis;

namespace DomainService.RequestModel
{
    public class UpdateSsoCredentialStatusRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public bool IsEnabled { get; set; }
        public string ProjectKey { get ; set ; }
    }
}
