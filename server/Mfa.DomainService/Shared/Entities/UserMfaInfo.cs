using Iam.DomainService.Entities;

namespace Mfa.DomainService.Shared
{
    public class UserMfaInfo
    {
        public bool MfaEnabled { get; set; }
        public bool IsMfaVerified { get; set; }
        public UserMfaType UserMfaType { get; set; }

    }
}
