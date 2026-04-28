using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Storage.RequestModel
{
    public class DeleteStorageConfigurationRequest : IProjectKey
    {
        public string ProjectKey { get; set; } = string.Empty;
        public string ConfigurationName { get; set; }
    }
}
