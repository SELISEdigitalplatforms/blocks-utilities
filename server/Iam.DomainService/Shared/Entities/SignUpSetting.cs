using Blocks.Genesis;

namespace Iam.DomainService.Shared.Entities
{
    public class SignUpSetting : BaseEntity
    {
        public bool IsEmailPasswordSignUpEnabled { get; set; }
        public bool IsSSoSignUpEnabled { get; set; }
    }
}
