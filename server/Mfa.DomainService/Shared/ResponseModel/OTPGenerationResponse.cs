using Blocks.Genesis;

namespace Mfa.DomainService.Shared
{
    public class OtpGenerationResponse : BaseResponse
    {
        public string MfaId { get; set; }
    }
}
