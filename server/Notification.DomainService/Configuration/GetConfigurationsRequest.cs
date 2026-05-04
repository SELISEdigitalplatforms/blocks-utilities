using Blocks.Genesis;

namespace DomainService.Configuration
{
    public class GetConfigurationsRequest : BaseGetsRequest<string>, IProjectKey
    {
        public string? ProjectKey { get ; set ; }
    }
}
