using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Mail.RequestModel
{
    public class DuplicateMailConfigurationRequest : IProjectKey
    {
        public string ConfigurationId { get; set; }
        public string ProjectKey { get; set; }
    }
}
