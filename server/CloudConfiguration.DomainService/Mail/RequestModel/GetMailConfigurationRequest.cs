using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Mail.RequestModel
{
    public class GetMailConfigurationRequest : IProjectKey
    {
        public string ConfigurationName { get; set; }
        public string ProjectKey { get; set; }
    }
}
