using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Mail.RequestModel
{
    public class GetAllMailConfigurationsRequest : IProjectKey
    {
        public string ProjectKey { get; set; }
    }
}
