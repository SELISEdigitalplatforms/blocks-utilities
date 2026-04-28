using Blocks.Genesis;

namespace Iam.DomainService.Users
{
    public class IsEmailAvaiableRequest : IProjectKey
    {
        public string Email { get; set; }
        public string? ProjectKey { get; set; }
    }

    public class IsEmailAvaiableResponse
    {
        public bool IsAvailable { get; set; }
    }
}
