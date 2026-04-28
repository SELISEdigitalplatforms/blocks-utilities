using Blocks.Genesis;

namespace Mfa.DomainService.Shared
{
    public class OtpVerificationResponse : BaseResponse
    {
        public bool IsValid { get; set; }
        public string UserId { get; set; }
    }
}
