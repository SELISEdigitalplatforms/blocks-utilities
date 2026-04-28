using Blocks.Genesis;

namespace Iam.DomainService.Users.RequestModel
{
    public class SaveSignUpSettingRequest : IProjectKey
    {
        public bool IsEmailPasswordSignUpEnabled { get; set; }
        public bool IsSSoSignUpEnabled { get; set; }
        public string ProjectKey { get ; set ; }
        public string? ItemId { get; set; }
    }
}
