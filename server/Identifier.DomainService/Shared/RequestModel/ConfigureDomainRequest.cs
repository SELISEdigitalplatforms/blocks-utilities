
using Blocks.Genesis;

namespace DomainService.Shared
{
    public class ConfigureDomainRequest : IProjectKey
    {
        public string ProjectKey { get; set; }
        public string CookieDomain { get; set; }
    }
}
