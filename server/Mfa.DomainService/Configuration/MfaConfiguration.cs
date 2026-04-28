using Blocks.Genesis;
using Iam.DomainService.Entities;

namespace Mfa.DomainService.Configuration
{
    public class MfaConfiguration : BaseEntity
    {
        public string Name { get; set; } = "Default";
        public bool EnableMfa { get; set; }
        public List<UserMfaType> UserMfaTypes { get; set; }
        public MfaTemplate MfaTemplate { get; set; }
    }
}
