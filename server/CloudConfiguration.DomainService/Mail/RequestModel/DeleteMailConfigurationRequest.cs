using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Mail.RequestModel
{
    public class DeleteMailConfigurationRequest : IProjectKey
    {
        public string ConfigurationId { get; set; }
        public string ProjectKey { get; set; }
    }
}
