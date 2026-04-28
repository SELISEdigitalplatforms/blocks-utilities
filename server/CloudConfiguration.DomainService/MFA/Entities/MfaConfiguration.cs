using Blocks.Genesis;
using CloudConfiguration.DomainService.MFA.Enums;

namespace CloudConfiguration.DomainService.MFA.Entities
{
    public class MfaConfiguration : BaseEntity
    {
        public string Name { get; set; } = "Default";
        public bool EnableMfa { get; set; }
        public List<CloudConfigurationUserMfaType> UserMfaTypes { get; set; }
        public MfaTemplate MfaTemplate { get; set; }
    }
}
