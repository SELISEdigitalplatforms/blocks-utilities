using Blocks.Genesis;
using Iam.DomainService.Entities;

namespace Mfa.DomainService.Configuration
{
    public class Configuration : IProjectKey
    {
        public bool EnableMfa { get; set; }
        public List<UserMfaType> UserMfaType { get; set; }
        public MfaTemplate? MfaTemplate { get; set; }
        public string ProjectKey { get; set; }
    }

    public class MfaTemplate
    {
        public string TemplateName { get; set; }
        public string TemplateId { get; set; }
    }
}
