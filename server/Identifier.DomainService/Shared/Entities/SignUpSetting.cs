using Blocks.Genesis;

namespace DomainService.Shared.Entities
{
    public class SignUpSetting : BaseEntity
    {
        public bool IsEmailPasswordSignUpEnabled { get; set; }
        public bool IsSSoSignUpEnabled { get; set; }
    }
}
