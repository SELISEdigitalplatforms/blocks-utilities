using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Storage.RequestModel
{
    public class GetStorageConfigurationRequest : IProjectKey
    {
        public string ProjectKey { get; set; } = string.Empty;
        public string ConfigurationName { get; set; }
    }
}
