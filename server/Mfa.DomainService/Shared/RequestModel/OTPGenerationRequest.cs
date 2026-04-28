using Blocks.Genesis;
using Iam.DomainService.Entities;

namespace Mfa.DomainService.Shared
{
    public class OtpGenerationRequest : IProjectKey
    {
        public string UserId { get; set; }
        public string? ProjectKey { get; set; }
        public UserMfaType? MfaType { get; set; }
        public string? SendPhoneNumberAsEmailDomain { get; set; }
    }
}
