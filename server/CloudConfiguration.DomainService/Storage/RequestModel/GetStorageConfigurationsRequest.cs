using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Storage.RequestModel
{
    public class GetStorageConfigurationsRequest : IProjectKey
    {
        public string ProjectKey { get; set; } = string.Empty;
    }
}
