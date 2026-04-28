
using Blocks.Genesis;
using Iam.DomainService.Entities;

namespace Mfa.DomainService.Shared
{
    public class VerifyOtpRequest : IProjectKey
    {
        public string VerificationCode { get; set; }
        public string MfaId { get; set; }
        public UserMfaType AuthType { get; set; }
        public string? ProjectKey { get; set; }
        public bool IsFromTokenCall { get; set; } = false;
    }
}
