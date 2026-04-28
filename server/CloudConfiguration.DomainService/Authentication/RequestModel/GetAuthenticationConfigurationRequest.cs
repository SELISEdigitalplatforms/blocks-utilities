using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Authentication.RequestModel
{
    public class GetAuthenticationConfigurationRequest : IProjectKey
    {
        public string ProjectKey { get; set; }
    }
}
