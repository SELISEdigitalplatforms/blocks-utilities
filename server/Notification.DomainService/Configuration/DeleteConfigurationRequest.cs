using Blocks.Genesis;

namespace DomainService.Configuration
{
    public class DeleteConfigurationRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public string ProjectKey { get; set; }
    }
}
